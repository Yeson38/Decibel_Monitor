using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Decibel_Monitor.Models;

/// <summary>
/// Decibel_Monitor 插件的全局设置（存放于插件配置目录，与组件/通知设置相互独立）。
/// </summary>
/// <remarks>
/// 提醒相关设置按<strong>判定源</strong>分组：每种"判断方式"一个判定源，各自带启用开关、
/// 冷却时间与专属参数；判定源的优先级顺序归仲裁模块（<c>AlertDecisionCoordinator</c>）。
/// <strong>不含通知/语音开关</strong>——强调通知与语音由 ClassIsland 通知提供方设置统一控制。
/// </remarks>
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

    // ── 平均音量判定（各判定源共用的输入平滑参数）────────────────────────

    /// <summary>
    /// 平均音量判定的滑动窗口时长（秒），默认 3 秒。
    /// 判定源比较的是窗口内的平均分贝而非瞬时值，避免单次尖峰误触发；窗口越长越平滑、响应越慢。
    /// </summary>
    [ObservableProperty] private double _averageWindowSeconds = 3.0;

    // ── 仲裁模块：判定源优先级顺序 ──────────────────────────────────────

    /// <summary>
    /// 判定源优先级顺序（按判定源 Id 排列，靠前者优先）。
    /// 同一次判定中多个判定源同时触发时，由靠前的判定源被记为触发者。默认自动提醒优先。
    /// </summary>
    [ObservableProperty] private List<string> _sourcePriorityOrder = new()
    {
        Alerting.AutoThresholdDecisionSource.SourceId,
        Alerting.HotkeyConfirmDecisionSource.SourceId,
    };

    // ── 时段闸门（对全部判定源生效）────────────────────────────────────

    /// <summary>
    /// 课间休息期间是否抑制提醒，默认 true。
    /// 抑制时<strong>只停止发出通知</strong>，状态点仍照常显示当前是否超阈值；
    /// 且抑制期间不推进判定源，因此不会占用冷却时间（课间误报不会影响上课时的提醒）。
    /// 课表未启用或未加载时本项不生效。
    /// </summary>
    [ObservableProperty] private bool _suppressAlertDuringBreak = true;

    /// <summary>
    /// 每节课开始后的保护时长（分钟），默认 3，取值按 0..10 夹取（消费端处理）；0 表示关闭该保护。
    /// 保护期内抑制提醒，用于避开上课起立、问好等固定声音造成的误报；同样不占用冷却时间。
    /// </summary>
    [ObservableProperty] private int _classStartProtectionMinutes = 3;

    // ── 判定源①：到阈值自动提醒 ─────────────────────────────────────────

    /// <summary>是否启用"自动提醒"判定源（超过阈值立即提醒）。</summary>
    [ObservableProperty] private bool _autoSourceEnabled;

    /// <summary>"自动提醒"判定源的阈值（显示刻度 0..150），默认 120。</summary>
    [ObservableProperty] private double _autoSourceThreshold = 120.0;

    /// <summary>"自动提醒"判定源的冷却时间（秒），默认 600 秒（10 分钟）。</summary>
    [ObservableProperty] private int _autoSourceCooldownSeconds = 600;

    // ── 判定源②：到阈值开筛选窗口，窗口内命中热键才提醒 ─────────────────

    /// <summary>是否启用"热键确认"判定源（需要 KeyboardCapture 插件提供按键事件）。</summary>
    [ObservableProperty] private bool _hotkeySourceEnabled;

    /// <summary>"热键确认"判定源的阈值（显示刻度 0..150），默认 120。</summary>
    [ObservableProperty] private double _hotkeySourceThreshold = 120.0;

    /// <summary>"热键确认"判定源的冷却时间（秒），默认 600 秒（10 分钟）。</summary>
    [ObservableProperty] private int _hotkeySourceCooldownSeconds = 600;

    /// <summary>"热键确认"判定源筛选窗口的开启时长（秒），默认 10 秒。</summary>
    [ObservableProperty] private int _hotkeyWindowSeconds = 10;

    /// <summary>筛选窗口内要求命中的键名（与 KeyboardCapture 的键名一致，如 F5、Space、A）。</summary>
    [ObservableProperty] private string _hotkeyKey = "F5";

    /// <summary>筛选窗口内要求命中的热键是否包含 Ctrl 修饰键。</summary>
    [ObservableProperty] private bool _hotkeyCtrl;

    /// <summary>筛选窗口内要求命中的热键是否包含 Alt 修饰键。</summary>
    [ObservableProperty] private bool _hotkeyAlt;

    /// <summary>筛选窗口内要求命中的热键是否包含 Shift 修饰键。</summary>
    [ObservableProperty] private bool _hotkeyShift;

    /// <summary>筛选窗口内要求命中的热键是否包含 Win（Meta）修饰键。</summary>
    [ObservableProperty] private bool _hotkeyMeta;
}
