using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Decibel_Monitor.Measurement;

/// <summary>
/// 常驻静默捕获流：以 WASAPI 共享模式持续读取默认麦克风峰值（仅计算峰值、不保存音频）。
/// 默认设备变化或捕获意外停止时自动重启（失败带退避）；流不可用时由调用方回退到系统实时计量。
/// 由 <see cref="AudioPeakMeter"/> 拆分而来，对外行为保持一致。
/// </summary>
internal sealed class ContinuousPeakCapture : IDisposable
{
    /// <summary>常驻捕获流启动失败后的重试退避间隔（毫秒）。</summary>
    private const int CaptureRestartBackoffMs = 5000;

    /// <summary>常驻捕获流的峰值窗口（毫秒）：超过该窗口后重新开始累计最大值。</summary>
    private static readonly TimeSpan ContinuousPeakWindow = TimeSpan.FromMilliseconds(400);

    private readonly object _captureLock = new();
    private readonly AudioDeviceEnumerator _devices;
    private readonly bool _enabled;

    private WasapiCapture? _continuousCapture;
    private string? _continuousCaptureDeviceId;
    private DateTime _continuousWindowStartUtc;
    private DateTime _nextCaptureStartAttemptUtc = DateTime.MinValue;
    private volatile bool _captureRunning;
    private volatile float _continuousPeak;
    private volatile bool _disposed;

    /// <summary>
    /// 初始化常驻捕获流。
    /// </summary>
    /// <param name="devices">设备枚举器（提供默认捕获设备）。</param>
    /// <param name="enabled">是否启用常驻捕获流。</param>
    public ContinuousPeakCapture(AudioDeviceEnumerator devices, bool enabled)
    {
        _devices = devices;
        _enabled = enabled;
    }

    /// <summary>当前峰值窗口内的最大线性峰值（0..1）。</summary>
    public float CurrentPeak => _continuousPeak;

    /// <summary>
    /// 确保"常驻静默捕获流"已启动。捕获流仅计算峰值、不保存音频；
    /// 若默认设备变化或捕获意外停止则自动重启（带退避）。
    /// </summary>
    public void EnsureStarted()
    {
        if (_disposed || !_enabled) return;

        // 捕获运行中：仅检查默认设备是否变化，变化则重启
        if (_captureRunning)
        {
            if (_continuousCaptureDeviceId != _devices.GetDefaultCaptureDevice()?.ID)
            {
                Restart();
            }
            return;
        }

        if (DateTime.UtcNow < _nextCaptureStartAttemptUtc) return; // 失败退避中

        lock (_captureLock)
        {
            if (_disposed || _captureRunning) return;
            if (DateTime.UtcNow < _nextCaptureStartAttemptUtc) return;

            try
            {
                var device = _devices.GetDefaultCaptureDevice();
                if (device is null)
                {
                    _nextCaptureStartAttemptUtc = DateTime.UtcNow.AddMilliseconds(CaptureRestartBackoffMs);
                    return;
                }

                StopLocked(); // 清理可能残留的对象

                var capture = new WasapiCapture(device);
                capture.DataAvailable += OnData;
                capture.RecordingStopped += OnStopped;
                capture.StartRecording();

                _continuousCapture = capture;
                _continuousCaptureDeviceId = device.ID;
                _continuousWindowStartUtc = DateTime.MinValue;
                _continuousPeak = 0f;
                _captureRunning = true;
            }
            catch
            {
                // 启动失败：清理并退避，避免高频重试
                StopLocked();
                _nextCaptureStartAttemptUtc = DateTime.UtcNow.AddMilliseconds(CaptureRestartBackoffMs);
            }
        }
    }

    /// <summary>
    /// 重启常驻捕获流（默认设备变化等场景）。
    /// </summary>
    private void Restart()
    {
        lock (_captureLock)
        {
            StopLocked();
            _nextCaptureStartAttemptUtc = DateTime.MinValue;
        }
        // 让 EnsureStarted 的加锁路径重新启动
        EnsureStarted();
    }

    /// <summary>
    /// 停止并释放当前常驻捕获流。须在持有 <see cref="_captureLock"/> 时调用。
    /// </summary>
    private void StopLocked()
    {
        var capture = _continuousCapture;
        _continuousCapture = null;
        _continuousCaptureDeviceId = null;
        _captureRunning = false;
        _continuousPeak = 0f;

        if (capture is null) return;
        try
        {
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnStopped;
            capture.StopRecording();
        }
        catch { /* 忽略停止时的异常 */ }
        capture.Dispose();
    }

    /// <summary>
    /// 常驻捕获流数据回调：在每个数据块中取峰值，并在 <see cref="ContinuousPeakWindow"/>
    /// 窗口内保留最大值（供读取线程经 <see cref="CurrentPeak"/> 获取）。
    /// </summary>
    private void OnData(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (sender is not WasapiCapture capture) return;

            var wf = capture.WaveFormat;
            float blockPeak = PeakSample.ComputePeak(
                e.Buffer, e.BytesRecorded,
                wf.Encoding == WaveFormatEncoding.IeeeFloat, wf.BitsPerSample);

            var now = DateTime.UtcNow;
            if (_continuousWindowStartUtc == DateTime.MinValue ||
                now - _continuousWindowStartUtc > ContinuousPeakWindow)
            {
                _continuousWindowStartUtc = now;
                _continuousPeak = blockPeak;
            }
            else if (blockPeak > _continuousPeak)
            {
                _continuousPeak = blockPeak;
            }
        }
        catch { /* 忽略单个数据块解析错误 */ }
    }

    /// <summary>
    /// 常驻捕获流停止回调（设备被拔出、被独占或异常等）。
    /// </summary>
    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        // 仅处理当前流的事件，忽略重启过程中旧流延迟到达的回调
        if (!ReferenceEquals(sender, _continuousCapture)) return;

        _captureRunning = false;
        _continuousPeak = 0f;
        if (e.Exception is not null)
        {
            _nextCaptureStartAttemptUtc = DateTime.UtcNow.AddMilliseconds(CaptureRestartBackoffMs);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_captureLock)
        {
            StopLocked();
        }
    }
}
