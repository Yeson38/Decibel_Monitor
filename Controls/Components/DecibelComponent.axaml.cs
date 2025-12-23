using System;
using System.ComponentModel;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using DecibelComponentSettings = Decibel_Monitor.Models.ComponentSettings.DecibelComponentSettings;
using NAudio.CoreAudioApi;

namespace Decibel_Monitor.Controls.Components;

[ComponentInfo(
    "FFFFFFFF-EEEE-DDDD-CCCC-BBBBBBBBBBBB",
    "分贝值",
    "\uE9B2",
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

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // 获取默认音频输入设备并读取峰值（范围 0..1）
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            if (defaultDevice?.AudioMeterInformation is null)
            {
                CurrentDecibelValue = "无设备";
                return;
            }

            float linear = defaultDevice.AudioMeterInformation.MasterPeakValue/2;

            // 若为 0 则视为静音 -> 映射为 0
            if (linear <= 0f)
            {
                CurrentDecibelValue = "0.0";
                return;
            }

            // 真实 dB（通常为负值或 0）
            double decibel = 20.0 * Math.Log10(linear);

            // 映射参数（可根据需求调整）
            const double minDb = -60.0;   // 小于该值视为 0
            const double maxDb = 0.0;     // 0 dB 对应映射上限
            const double targetMax = 150.0; // 输出的最大值
            const double gamma = 1.2;     // >1 压缩高位，<1 扩张高位

            // 限制并归一化到 0..1
            double clampedDb = Math.Max(minDb, Math.Min(maxDb, decibel));
            double normalized = (clampedDb - minDb) / (maxDb - minDb);

            // 应用 gamma 曲线以调整感知（使中高值不至于过大）
            double adjusted = Math.Pow(normalized, gamma);

            double mapped = adjusted * targetMax;

            CurrentDecibelValue = $"{mapped:F1}";
        }
        catch (Exception)
        {
            // 任何异常都不会抛回 UI，显示占位文本
            CurrentDecibelValue = "读取失败";
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