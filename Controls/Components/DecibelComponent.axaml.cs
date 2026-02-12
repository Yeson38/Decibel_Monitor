using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DecibelComponentSettings = Decibel_Monitor.Models.ComponentSettings.DecibelComponentSettings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Decibel_Monitor.Controls.Components;

[ComponentInfo(
    "FFFFFFFF-EEEE-DDDD-CCCC-BBBBBBBBBBBB",
    "分贝值",
    "\uEB88",
    "在主界面上显示麦克风分贝值。"
)]
public partial class DecibelComponent : ComponentBase<DecibelComponentSettings>, INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _updateTimer;
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;
    private string _currentDecibelValue = "N/A";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentDecibelValue
    {
        get => _currentDecibelValue;
        private set
        {
            if (_currentDecibelValue == value) return;
            _currentDecibelValue = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDecibelValue)));
        }
    }

    public DecibelComponent()
    {
        InitializeComponent();

        _enumerator = new MMDeviceEnumerator();

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    // 异步 Tick，内部会异步采样（不会阻塞 UI）
    private async void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var captureDevices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var selectedDevice = captureDevices.FirstOrDefault(c => c.ID == defaultDevice.ID) ?? defaultDevice;

            if (selectedDevice?.AudioMeterInformation is null)
            {
                CurrentDecibelValue = "无设备";
                return;
            }

            // 获取线性峰值（0..1），内部含多重回退采样
            float linear = await GetVoicePeakLinearAsync(selectedDevice).ConfigureAwait(false);

            // 应用放大倍数（保护 Settings 为空）
            double magnification = Settings?.Magnification ?? 1.0;
            linear = (float)(linear * magnification);

            if (linear <= 0f)
            {
                CurrentDecibelValue = "0.0";
                return;
            }

            // 计算 dBFS（通常为负值）
            double dbfs = 20.0 * Math.Log10(linear);

            // 映射为 0..150：使用 150 + dBFS，再 clamp 到 0..150
            double mapped = Math.Clamp(150.0 + dbfs, 0.0, 150.0);

            CurrentDecibelValue = $"{mapped:F1}";
        }
        catch (Exception)
        {
            CurrentDecibelValue = "读取失败";
        }
    }

    // 与设置控件类似的多重回退采样函数（优先 AudioMeterInformation -> WasapiCapture -> WaveIn）
    private async Task<float> GetVoicePeakLinearAsync(MMDevice selected)
    {
        try
        {
            // 1) 尝试 AudioMeterInformation
            var val = selected?.AudioMeterInformation?.MasterPeakValue ?? 0f;
            if (val > 0.0001f) return val;

            // 2) 尝试 WasapiCapture 指定设备
            try
            {
                var v = await GetVoicePeakLinearByWasapiCaptureAsync(selected, 300).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { }

            // 3) 尝试 WasapiCapture 默认设备
            try
            {
                var v = await GetVoicePeakLinearByWasapiCaptureAsync(null, 300).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { }

            // 4) 尝试 WaveInEvent 回退
            try
            {
                var v = await GetVoicePeakLinearByWaveInAsync(300).ConfigureAwait(false);
                if (v > 0.0001f) return v;
            }
            catch { }

            return 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private async Task<float> GetVoicePeakLinearByWasapiCaptureAsync(MMDevice? device, int captureMs = 300)
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
                catch { }
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

    private async Task<float> GetVoicePeakLinearByWaveInAsync(int captureMs = 300)
    {
        try
        {
            float maxSample = 0f;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer.Tick -= UpdateTimer_Tick;
        _updateTimer.Stop();

        _enumerator.Dispose();
    }
}