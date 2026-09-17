using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models.NotificationProviderSettings;

/// <summary>
/// 分贝提醒（通知提供方）的设置。由 ClassIsland 宿主负责持久化。
/// 对应"强调通知侧"的设置项：提醒开关、阈值、正文与冷却时间。
/// </summary>
public partial class DecibelNotificationProviderSettings : ObservableObject
{
    /// <summary>
    /// 是否启用分贝阈值提醒。
    /// </summary>
    [ObservableProperty] private bool _isAlertEnabled = false;

    /// <summary>
    /// 提醒阈值（显示刻度 0..150），超过该值触发提醒。
    /// </summary>
    [ObservableProperty] private double _alertThreshold = 120.0;

    /// <summary>
    /// 提醒正文（通知横幅显示的文字）。
    /// </summary>
    [ObservableProperty] private string _alertText = "请保持安静";

    /// <summary>
    /// 触发提醒后的冷却时间（分钟），避免频繁提醒，默认 10 分钟。
    /// </summary>
    [ObservableProperty] private int _alertCooldownMinutes = 10;
}
