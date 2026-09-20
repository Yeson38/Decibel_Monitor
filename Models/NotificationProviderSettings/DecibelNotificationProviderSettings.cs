using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models.NotificationProviderSettings;

/// <summary>
/// 分贝提醒（通知提供方）的设置。由 ClassIsland 宿主负责持久化。
/// 对应"强调通知侧"的设置项：提醒正文。
/// </summary>
/// <remarks>
/// 阈值、冷却与启用开关由插件设置页中的"判定源"负责（见 DecibelMonitorGlobalSettings）：
/// 判定源决定"何时提醒"，本通知提供方只负责"发什么内容"，不重复做阈值判定，
/// 避免出现"判定源已裁定提醒、提供方却因自身阈值/冷却不发通知"的双重门槛。
/// </remarks>
public partial class DecibelNotificationProviderSettings : ObservableObject
{
    /// <summary>
    /// 提醒正文（通知横幅显示的文字）。
    /// </summary>
    [ObservableProperty] private string _alertText = "请保持安静";
}
