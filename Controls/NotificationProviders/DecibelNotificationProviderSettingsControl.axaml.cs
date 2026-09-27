using ClassIsland.Core.Abstractions.Controls;
using Decibel_Monitor.Models.NotificationProviderSettings;

namespace Decibel_Monitor.Controls.NotificationProviders;

/// <summary>
/// 分贝提醒通知提供方的设置控件（"强调通知侧"）。
/// </summary>
public partial class DecibelNotificationProviderSettingsControl : NotificationProviderControlBase<DecibelNotificationProviderSettings>
{
    public DecibelNotificationProviderSettingsControl()
    {
        InitializeComponent();
    }
}
