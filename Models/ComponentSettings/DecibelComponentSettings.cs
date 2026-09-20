using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models.ComponentSettings;

/// <summary>
/// 分贝组件的设置。每个组件实例独立持久化，仅保留与"小组件显示侧"相关的显示偏好。
/// 测量/校准（参考 dB、放大倍数）与提醒判定（各判定源的启用/阈值/冷却等）为全局设置，
/// 位于插件设置页（DecibelMonitorGlobalSettings）；提醒正文在通知提供方设置
/// （DecibelNotificationProviderSettings）。
/// </summary>
public partial class DecibelComponentSettings : ObservableObject
{
    /// <summary>
    /// 是否在组件上显示提醒状态点（默认 true）。
    /// 绿点=正常；红点=提醒触发中（超过阈值、筛选窗口开启或处于冷却期）。
    /// 关闭后仍会正常发出系统通知，只是不在组件上显示状态点。
    /// </summary>
    [ObservableProperty] private bool _showStatusIndicator = true;

    /// <summary>
    /// 是否在数值前显示“分贝:”前缀（如“分贝: 70.0”），默认 true。
    /// </summary>
    [ObservableProperty] private bool _showDecibelPrefix = true;

    /// <summary>
    /// 数值更新频率（毫秒，100..5000），默认 200。
    /// 越小响应越快但刷新开销越高；常驻捕获流读取下一般 100..500 即可。
    /// </summary>
    [ObservableProperty] private int _updateIntervalMs = 200;
}
