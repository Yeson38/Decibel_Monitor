using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models;

/// <summary>
/// Decibel_Monitor 插件的全局设置（存放于插件配置目录，与组件/通知设置相互独立）。
/// </summary>
public partial class DecibelMonitorGlobalSettings : ObservableObject
{
    /// <summary>
    /// 校准参考（显示刻度 dB 0..150，换算为 dBFS 时为其减去 150），默认 70（对应 -80 dBFS）。
    /// 同一麦克风的测量/校准为全局一致，故作为插件级全局设置。
    /// </summary>
    [ObservableProperty] private double _referenceDecibel = 70.0;

    /// <summary>
    /// 全局放大倍数：由校准计算后写入，组件显示分贝时统一使用该值，避免各组件数值不一致。
    /// </summary>
    [ObservableProperty] private double _magnification = 1.0;

    /// <summary>
    /// 是否启用"常驻静默捕获流"，默认 true。
    /// 开启后保持一条低开销的麦克风捕获流以持续获取峰值（Windows 会显示"麦克风正在使用"，但图标常亮、不会闪烁）；
    /// 关闭后仅使用系统实时计量（多数设备在无活跃录音会话时该值为 0，组件可能长期显示 0）。
    /// </summary>
    [ObservableProperty] private bool _enableContinuousMonitoring = true;

    /// <summary>
    /// 显式采样（如校准）的单次录音时长（毫秒）。较长时间可捕获更低频率/更弱信号。
    /// </summary>
    [ObservableProperty] private int _fallbackCaptureMs = 400;

    /// <summary>
    /// 线性峰值信号检测阈值：超过该值视为"检测到有效信号"（可在采样异常时调低）。
    /// </summary>
    [ObservableProperty] private double _signalThreshold = 0.0001;
}
