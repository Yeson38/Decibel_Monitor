using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using Decibel_Monitor.Models.ComponentSettings;
using NAudio.CoreAudioApi;

namespace Decibel_Monitor.Controls.ComponentSettings;

// <summary>
// 分贝组件设置控件类，用于管理分贝监测组件的相关配置界面
// 继承自ComponentBase基类，使用DecibelComponentSettings作为泛型参数
// </summary>
public partial class DecibelComponentSettingsControl : ComponentBase<DecibelComponentSettings>, INotifyPropertyChanged, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    private double _referenceDecibel = 94.0; // 默认参考值，可在 UI 上调整

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

    // <summary>
    // 构造函数，初始化分贝组件设置控件
    // 调用InitializeComponent方法加载XAML界面元素
    // </summary>
    public DecibelComponentSettingsControl()
    {
        InitializeComponent();
        _enumerator = new MMDeviceEnumerator();

        try
        {
            // 临时调试：确认构造被调用。可替换为日志或删除。
            ClassIsland.Core.Controls.CommonTaskDialogs.ShowDialog("调试", "DecibelComponentSettingsControl 已构造");
        }
        catch { /* 宿主可能在非 UI 线程，忽略异常 */ }
    }

    // 供校准使用：返回当前默认音频输入设备的线性峰值（0..1）
    public float GetVoicePeakLinear()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var linearValue = defaultDevice?.AudioMeterInformation?.MasterPeakValue ?? 0f;
            enumerator.Dispose();
            return linearValue;
        }
        catch
        {
            return 0f;
        }
    }

    // 校准按钮事件：读取当前线性峰值，按公式计算放大倍数并保存到 Settings.Magnification
    // 公式： measuredDb = 20 * log10(linear)
    //       magnification = 10^((targetDb - measuredDb) / 20)
    public void On_Calibrate(object? sender, RoutedEventArgs e)
    {
        try
        {
            float linear = GetVoicePeakLinear();

            if (linear <= 0f)
            {
                // 无有效读数，保持默认或设置为 1.0
                Settings.Magnification = 1.0;
                return;
            }

            double measuredDb = 20.0 * Math.Log10(linear);
            double targetDb = ReferenceDecibel;

            // 计算放大倍数
            double magnification = Math.Pow(10.0, (targetDb - measuredDb) / 20.0);

            // 防止极端值，做合理约束（例如 0.001 .. 1000）
            magnification = Math.Clamp(magnification, 0.001, 1000.0);

            Settings.Magnification = magnification;
        }
        catch
        {
            // 出错时保留原设置，不抛出
            Settings.Magnification = Settings.Magnification;
        }
    }

    // 保留原来（若需）打开文件方法（已注释）
    /*private async void On_OpenFile(object sender, RoutedEventArgs e)
    {
        // ... 原有实现（如需启用请恢复）
    }*/

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.Dispose();
    }
}