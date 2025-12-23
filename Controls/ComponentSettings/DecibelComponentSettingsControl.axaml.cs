using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls;
using Decibel_Monitor.Models.ComponentSettings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Decibel_Monitor.Controls.ComponentSettings;

// <summary>
// 分贝组件设置控件类，用于管理分贝监测组件的相关配置界面
// 继承自ComponentBase基类，使用DecibelComponentSettings作为泛型参数
// </summary>
public partial class DecibelComponentSettingsControl : ComponentBase<DecibelComponentSettings>, INotifyPropertyChanged, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    // 默认改为 -80 dBFS（用户可在 UI 上改为 -150..0 范围任意值）
    private double _referenceDecibel = -80.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double ReferenceDecibel
    {
        get => _referenceDecibel;
        set
        {
            if (Math.Abs(_referenceDecibel - value) < 0.0001) return;
            _referenceDecibel = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReferenceDecibel)));
        }
    }

    public DecibelComponentSettingsControl()
    {
        InitializeComponent();
        _enumerator = new MMDeviceEnumerator();
    }

    // 可选：列出系统捕获设备用于诊断（调试时调用）
    private void ShowAvailableCaptureDevices()
    {
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                                     .Select(d => $"{d.FriendlyName} ({d.ID})");
            var text = string.Join("\n", devices);
            CommonTaskDialogs.ShowDialog("捕获设备列表", text);
        }
        catch
        {
            /* 忽略诊断失败 */
        }
    }

    // 异步版：返回当前默认音频输入设备的线性峰值（0..1）
    // 增加多重回退：MMDevice.AudioMeterInformation -> WasapiCapture(device) -> WasapiCapture() -> WaveInEvent
    public async Task<float> GetVoicePeakLinearAsync()
    {
        try
        {
            var captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var selected = captureDevices.FirstOrDefault(c => c.ID == defaultDevice.ID) ?? defaultDevice;

            // 1) 先读 AudioMeterInformation（低成本）
            var linearValue = selected?.AudioMeterInformation?.MasterPeakValue ?? 0f;
            if (linearValue > 0.0001f)
            {
                return linearValue;
            }

            // 2) 尝试 WasapiCapture 指定设备
            try
            {
                var v = await GetVoicePeakLinearByWasapiCaptureAsync(selected, 600).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { /* 忽略，继续回退 */ }

            // 3) 尝试 WasapiCapture 默认设备（无 device 参数）
            try
            {
                var v = await GetVoicePeakLinearByWasapiCaptureAsync(null, 600).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { /* 忽略 */ }

            // 4) 尝试 WaveInEvent（旧 API，兼容性更好）
            try
            {
                var v = await GetVoicePeakLinearByWaveInAsync(600).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { /* 忽略 */ }

            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    // 使用 WasapiCapture（可传入 MMDevice 或 null 表示默认设备）
    private async Task<float> GetVoicePeakLinearByWasapiCaptureAsync(MMDevice? device, int captureMs = 600)
    {
        try
        {
            float maxSample = 0f;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var capture = device is null ? new WasapiCapture() : new WasapiCapture(device);

            capture.DataAvailable += (s, e) =>
            {
                try
                {
                    var wf = capture.WaveFormat;
                    if (wf.Encoding == WaveFormatEncoding.IeeeFloat)
                    {
                        for (int n = 0; n + 4 <= e.BytesRecorded; n += 4)
                        {
                            float sample = BitConverter.ToSingle(e.Buffer, n);
                            maxSample = Math.Max(maxSample, Math.Abs(sample));
                        }
                    }
                    else if (wf.BitsPerSample == 16)
                    {
                        for (int n = 0; n + 2 <= e.BytesRecorded; n += 2)
                        {
                            short s16 = BitConverter.ToInt16(e.Buffer, n);
                            float sample = Math.Abs(s16 / 32768f);
                            maxSample = Math.Max(maxSample, sample);
                        }
                    }
                    else if (wf.BitsPerSample == 32)
                    {
                        for (int n = 0; n + 4 <= e.BytesRecorded; n += 4)
                        {
                            int i32 = BitConverter.ToInt32(e.Buffer, n);
                            float sample = Math.Abs(i32 / (float)int.MaxValue);
                            maxSample = Math.Max(maxSample, sample);
                        }
                    }
                }
                catch
                {
                }
            };

            capture.RecordingStopped += (s, e) => tcs.TrySetResult(true);

            capture.StartRecording();
            await Task.Delay(captureMs).ConfigureAwait(false);
            capture.StopRecording();
            await Task.WhenAny(tcs.Task, Task.Delay(1000)).ConfigureAwait(false);

            return Math.Clamp(maxSample, 0f, 1f);
        }
        catch
        {
            return 0f;
        }
    }

    // 使用 WaveInEvent 作为最后回退（与系统默认设备交互良好）
    private async Task<float> GetVoicePeakLinearByWaveInAsync(int captureMs = 600)
    {
        try
        {
            float maxSample = 0f;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(16000, 16, 1); // 单声道 16k 16bit
            waveIn.DataAvailable += (s, e) =>
            {
                try
                {
                    for (int i = 0; i + 2 <= e.BytesRecorded; i += 2)
                    {
                        short s16 = BitConverter.ToInt16(e.Buffer, i);
                        float sample = Math.Abs(s16 / 32768f);
                        maxSample = Math.Max(maxSample, sample);
                    }
                }
                catch { }
            };
            waveIn.RecordingStopped += (s, e) => tcs.TrySetResult(true);

            waveIn.StartRecording();
            await Task.Delay(captureMs).ConfigureAwait(false);
            waveIn.StopRecording();
            await Task.WhenAny(tcs.Task, Task.Delay(1000)).ConfigureAwait(false);

            return Math.Clamp(maxSample, 0f, 1f);
        }
        catch
        {
            return 0f;
        }
    }

    // 异步事件处理：避免阻塞 UI 线程
    public async void On_Calibrate(object? sender, RoutedEventArgs e)
    {
        try
        {
            float measuredLinear = await GetVoicePeakLinearAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (measuredLinear <= 0f)
                {
                    try { CommonTaskDialogs.ShowDialog("校准结果", "未检测到有效输入，无法完成校准。请确认麦克风已启用并有声源。"); } catch { }
                    return;
                }

                double targetDb = ReferenceDecibel; // 现在为 dBFS
                double targetLinear = Math.Pow(10.0, targetDb / 20.0);

                double magnification = targetLinear / measuredLinear;
                // 限制放大倍数上限为 1024，下限 0
                magnification = Math.Clamp(magnification, 0, 1024.0);

                double adjustedLinear = measuredLinear * magnification;
                double adjustedDb = adjustedLinear > 0 ? 20.0 * Math.Log10(adjustedLinear) : double.NegativeInfinity;
                double measuredDb = measuredLinear > 0 ? 20.0 * Math.Log10(measuredLinear) : double.NegativeInfinity;

                if (Settings is not null)
                {
                    Settings.Magnification = magnification;
                    try
                    {
                        CommonTaskDialogs.ShowDialog("校准结果",
                            $"测量线性值: {measuredLinear:F4}\n" +
                            $"测量 dBFS: {measuredDb:F1} dB\n" +
                            $"目标 dBFS: {targetDb:F1} dB\n" +
                            $"放大倍数: {magnification:F6}×\n" +
                            $"放大后 dBFS (验证): {adjustedDb:F1} dB");
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        CommonTaskDialogs.ShowDialog("校准完成（未保存）",
                            $"测量线性值: {measuredLinear:F4}\n" +
                            $"测量 dBFS: {measuredDb:F1} dB\n" +
                            $"目标 dBFS: {targetDb:F1} dB\n" +
                            $"建议放大倍数: {magnification:F6}×\n" +
                            $"放大后 dBFS (验证): {adjustedDb:F1} dB\n\n" +
                            "当前控件未绑定到组件实例，无法直接保存设置。");
                    }
                    catch { }
                }
            });
        }
        catch (Exception ex)
        {
            try { CommonTaskDialogs.ShowDialog("校准失败", $"发生异常：{ex.Message}"); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.Dispose();
    }
}