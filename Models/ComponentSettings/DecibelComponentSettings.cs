using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models.ComponentSettings;

/// <summary>
/// 分贝组件的设置。每个组件实例独立持久化，仅保留与"小组件显示侧"相关的显示偏好。
/// 测量/校准（参考 dB、放大倍数）为全局设置，位于插件设置页（DecibelMonitorGlobalSettings）；
/// 提醒相关设置在通知提供方设置（DecibelNotificationProviderSettings）。
/// </summary>
public partial class DecibelComponentSettings : ObservableObject
{
    /// <summary>
    /// 是否在组件上显示"超过阈值"的提示文字（如“请保持安静”），默认 true。
    /// 关闭后超阈值仍会发送系统通知，但不在组件上显示红色提示文字。
    /// </summary>
    [ObservableProperty] private bool _showAlertTextOnComponent = true;

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
