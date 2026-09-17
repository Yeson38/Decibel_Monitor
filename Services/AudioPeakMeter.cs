using System;
using System.Threading;
using System.Threading.Tasks;

namespace Decibel_Monitor.Services;

/// <summary>
/// 麦克风峰值采样服务。
/// 通过一条"常驻静默捕获流"（WASAPI 共享模式，仅计算峰值、不保存音频）持续读取默认麦克风峰值，
/// 供组件高频刷新直接读取——数值稳定、无周期性打开/关闭录音导致的占用图标闪烁。
/// 常驻流不可用时回退到系统实时计量（AudioMeterInformation）；
/// 显式操作（校准等）可调用 <see cref="CaptureSamplePeakAsync"/> 临时采样一次。
/// 设备枚举结果带缓存，采样会话（临时采样）受信号量保护。
/// </summary>
/// <remarks>
/// 实现已拆分为：<see cref="AudioDeviceEnumerator"/>（设备枚举与缓存）、
/// <see cref="ContinuousPeakCapture"/>（常驻捕获流）、<see cref="OneShotPeakCapture"/>（显式一次性采样）。
/// 本类只负责持有配置并转发调用，公开 API 保持不变。
/// </remarks>
public sealed class AudioPeakMeter : IDisposable
{
    private readonly AudioDeviceEnumerator _devices = new();
    private readonly ContinuousPeakCapture _continuousCapture;
    private readonly OneShotPeakCapture _oneShotCapture;

    /// <summary>临时（显式）采样单次录音时长（毫秒），来自插件全局设置，默认 400。</summary>
    private readonly int _fallbackCaptureMs = 400;

    /// <summary>线性峰值超过该阈值视为"检测到有效信号"，来自插件全局设置，默认 0.0001。</summary>
    private readonly float _signalThreshold = 0.0001f;

    /// <summary>
    /// 是否启用"常驻静默捕获流"。默认 true：保持一条低开销捕获流以持续获取峰值
    /// （Windows 会显示麦克风正在使用且图标常亮，但不会闪烁）。
    /// 关闭后仅使用系统实时计量（多数设备在无活跃录音会话时该值为 0）。
    /// </summary>
    private readonly bool _enableContinuousMonitoring;

    private volatile bool _disposed;

    /// <summary>当前服务是否已释放。</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 当前默认的临时采样时长（毫秒，来自插件全局设置；未注入时 400）。
    /// </summary>
    public int DefaultCaptureMs => _fallbackCaptureMs;

    /// <summary>
    /// 是否启用"常驻静默捕获流"。
    /// </summary>
    public bool EnableContinuousMonitoring => _enableContinuousMonitoring;

    /// <summary>
    /// 初始化采样服务。
    /// </summary>
    /// <param name="settingsService">插件全局设置服务（可空；缺省时使用内置默认采样参数）。</param>
    public AudioPeakMeter(DecibelMonitorSettingsService? settingsService = null)
    {
        if (settingsService is not null)
        {
            _fallbackCaptureMs = Math.Clamp(settingsService.Settings.FallbackCaptureMs, 100, 5000);
            _signalThreshold = (float)Math.Clamp(settingsService.Settings.SignalThreshold, 0.000001, 1.0);
            _enableContinuousMonitoring = settingsService.Settings.EnableContinuousMonitoring;
        }

        _continuousCapture = new ContinuousPeakCapture(_devices, _enableContinuousMonitoring);
        _oneShotCapture = new OneShotPeakCapture(_devices, _signalThreshold);
    }

    /// <summary>
    /// 快速读取默认捕获设备的实时线性峰值（0..1）。
    /// 仅读取 AudioMeterInformation，不做耗时采样，适合高频 UI 刷新。
    /// 若设备不支持实时计量或读取失败，返回 0。
    /// </summary>
    public float GetDefaultDevicePeakLinearFast()
    {
        if (_disposed) return 0f;
        try
        {
            var device = _devices.GetDefaultCaptureDevice();
            return device?.AudioMeterInformation?.MasterPeakValue ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// 获取默认捕获设备的线性峰值（0..1），供组件周期刷新使用。
    /// 启用"常驻静默捕获流"时，返回常驻流在当前峰值窗口内的最大值（稳定、无周期性开/关录音）；
    /// 同时优先返回实时计量（AudioMeterInformation）读到的更大瞬时值以提高响应灵敏度。
    /// 未启用常驻流时仅使用系统实时计量（多数设备在无活跃录音会话时该值为 0）。
    /// </summary>
    public Task<float> GetDefaultDevicePeakLinearAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult(0f);

        var fast = GetDefaultDevicePeakLinearFast();
        if (fast > _signalThreshold)
        {
            return Task.FromResult(fast);
        }

        if (_enableContinuousMonitoring)
        {
            _continuousCapture.EnsureStarted();
            return Task.FromResult(_continuousCapture.CurrentPeak);
        }

        return Task.FromResult(0f);
    }

    /// <summary>
    /// 显式采样一次默认捕获设备的线性峰值（0..1）——用户主动触发（如校准、诊断）时使用。
    /// 会按 AudioMeterInformation → WasapiCapture → WaveInEvent 顺序尝试，
    /// 期间会短暂打开录音通道（受信号量保护，同一时间仅一个采样会话）。
    /// </summary>
    /// <param name="captureMs">录音采样时长（毫秒），限制在 100..5000 之间。</param>
    public async Task<float> CaptureSamplePeakAsync(int captureMs, CancellationToken cancellationToken = default)
    {
        if (_disposed) return 0f;
        return await _oneShotCapture.CaptureAsync(captureMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取当前可用捕获设备的描述列表（用于诊断显示）。
    /// </summary>
    public string[] GetCaptureDeviceDescriptions()
    {
        if (_disposed) return Array.Empty<string>();
        return _devices.GetCaptureDeviceDescriptions();
    }

    /// <summary>
    /// 获取默认捕获设备的描述（用于诊断显示）。
    /// </summary>
    public string? GetDefaultCaptureDeviceDescription()
    {
        if (_disposed) return null;
        return _devices.GetDefaultCaptureDeviceDescription();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _continuousCapture.Dispose();
        _oneShotCapture.Dispose();
        _devices.Dispose();
    }
}
