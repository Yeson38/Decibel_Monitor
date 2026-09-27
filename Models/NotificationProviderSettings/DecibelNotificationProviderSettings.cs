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

    /// <summary>
    /// 提醒正文（第二段）的显示时长（秒），取值 1~600。
    /// </summary>
    /// <remarks>
    /// 一次提醒由两段拼成：遮罩固定为宿主 <see cref="ClassIsland.Core.Models.Notification.NotificationContent"/>
    /// 的默认时长（5 秒），正文时长由本项决定，因此一次提醒的总时长 = 5 秒 + 本值。
    /// 两段文案相同，肉眼看上去像一段；总时长短于判定源的冷却时间时，才能在提醒结束后看到冷却期的状态点。
    /// </remarks>
    [ObservableProperty] private int _overlayDurationSeconds = 30;
}
