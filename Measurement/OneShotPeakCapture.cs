using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Decibel_Monitor.Measurement;

/// <summary>
/// 显式（一次性）采样：按 AudioMeterInformation → WasapiCapture → WaveInEvent 顺序回退，
/// 采样会话受信号量保护（同一时间仅一个会话），期间会短暂打开录音通道。
/// 由 <see cref="AudioPeakMeter"/> 拆分而来，对外行为保持一致。
/// </summary>
internal sealed class OneShotPeakCapture : IDisposable
{
    private readonly SemaphoreSlim _samplingGate = new(1, 1);
    private readonly AudioDeviceEnumerator _devices;
    private readonly float _signalThreshold;
    private volatile bool _disposed;

    /// <summary>
    /// 初始化一次性采样器。
    /// </summary>
    /// <param name="devices">设备枚举器（提供默认捕获设备）。</param>
    /// <param name="signalThreshold">线性峰值超过该值视为“检测到有效信号”。</param>
    public OneShotPeakCapture(AudioDeviceEnumerator devices, float signalThreshold)
    {
        _devices = devices;
        _signalThreshold = signalThreshold;
    }

    /// <summary>
    /// 显式采样一次默认捕获设备的线性峰值（0..1）——用户主动触发（如校准、诊断）时使用。
    /// </summary>
    /// <param name="captureMs">录音采样时长（毫秒），限制在 100..5000 之间。</param>
    public async Task<float> CaptureAsync(int captureMs, CancellationToken cancellationToken = default)
    {
        if (_disposed) return 0f;

        await _samplingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var device = _devices.GetDefaultCaptureDevice();
            return await GetPeakLinearAsync(device, Math.Clamp(captureMs, 100, 5000), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _samplingGate.Release();
        }
    }

    /// <summary>
    /// 获取指定设备（可空表示默认设备）的线性峰值（0..1），按 AudioMeterInformation → WasapiCapture → WaveInEvent 顺序回退。
    /// </summary>
    private async Task<float> GetPeakLinearAsync(MMDevice? device, int captureMs, CancellationToken cancellationToken)
    {
        // 1) AudioMeterInformation（低成本实时计量）
        var meterValue = device?.AudioMeterInformation?.MasterPeakValue ?? 0f;
        if (meterValue > _signalThreshold) return meterValue;

        // 2) WasapiCapture（指定设备）
        try
        {
            var v = await GetPeakByWasapiCaptureAsync(device, captureMs, cancellationToken).ConfigureAwait(false);
            if (v > _signalThreshold) return v;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 忽略，继续回退 */ }

        // 3) WasapiCapture（默认设备）：仅在指定设备并非默认设备时才需要再次尝试，
        //    避免对同一设备重复录音造成不必要的开销。
        if (!_devices.IsDefaultDevice(device))
        {
            try
            {
                var v = await GetPeakByWasapiCaptureAsync(null, captureMs, cancellationToken).ConfigureAwait(false);
                if (v > _signalThreshold) return v;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 忽略 */ }
        }

        // 4) WaveInEvent（旧 API，兼容性更好）
        try
        {
            var v = await GetPeakByWaveInAsync(captureMs, cancellationToken).ConfigureAwait(false);
            if (v > _signalThreshold) return v;
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 忽略 */ }

        return 0f;
    }

    /// <summary>
    /// 使用 WasapiCapture 短时录音并计算最大样本绝对值（0..1）。
    /// 检测到首个有效信号即提前返回，避免等满整个录音时长。
    /// </summary>
    private async Task<float> GetPeakByWasapiCaptureAsync(MMDevice? device, int captureMs, CancellationToken cancellationToken)
    {
        float maxSample = 0f;
        var tcs = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var capture = device is null ? new WasapiCapture() : new WasapiCapture(device);
        capture.DataAvailable += (s, e) =>
        {
            try
            {
                var wf = capture.WaveFormat;
                float peak = PeakSample.ComputePeak(
                    e.Buffer, e.BytesRecorded,
                    wf.Encoding == WaveFormatEncoding.IeeeFloat, wf.BitsPerSample);

                if (peak > maxSample) maxSample = peak;
                // 检测到有效信号即可提前完成采样，避免等满整个录音时长
                if (maxSample > _signalThreshold) tcs.TrySetResult(maxSample);
            }
            catch { /* 忽略单个数据块解析错误 */ }
        };
        capture.RecordingStopped += (s, e) => tcs.TrySetResult(maxSample);

        capture.StartRecording();
        try
        {
            // 首个有效信号或录音时长到点即返回
            await Task.WhenAny(tcs.Task, Task.Delay(captureMs, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            capture.StopRecording();
        }

        // 采样结果经 TCS 传递，保证回调线程写入对当前线程可见
        var peak = tcs.Task.IsCompleted ? await tcs.Task.ConfigureAwait(false) : maxSample;
        return Math.Clamp(peak, 0f, 1f);
    }

    /// <summary>
    /// 使用 WaveInEvent（旧 API）短时录音并计算最大样本绝对值（0..1）。
    /// 检测到首个有效信号即提前返回，避免等满整个录音时长。
    /// </summary>
    private async Task<float> GetPeakByWaveInAsync(int captureMs, CancellationToken cancellationToken)
    {
        float maxSample = 0f;
        var tcs = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var waveIn = new WaveInEvent();
        waveIn.WaveFormat = new WaveFormat(16000, 16, 1); // 单声道 16k 16bit
        waveIn.DataAvailable += (s, e) =>
        {
            try
            {
                float peak = PeakSample.ComputePeak(e.Buffer, e.BytesRecorded, false, 16);
                if (peak > maxSample) maxSample = peak;
                if (maxSample > _signalThreshold) tcs.TrySetResult(maxSample);
            }
            catch { }
        };
        waveIn.RecordingStopped += (s, e) => tcs.TrySetResult(maxSample);

        waveIn.StartRecording();
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(captureMs, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            waveIn.StopRecording();
        }

        var peak = tcs.Task.IsCompleted ? await tcs.Task.ConfigureAwait(false) : maxSample;
        return Math.Clamp(peak, 0f, 1f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _samplingGate.Dispose();
    }
}
