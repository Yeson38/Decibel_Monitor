using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 判定源②：到阈值开筛选窗口，窗口内命中指定热键才提醒（无输入不提醒）。
/// </summary>
/// <remarks>
/// 判定流程：
/// <list type="number">
/// <item>窗口未开启时：平均分贝超过阈值且自身冷却已过 → <strong>打开筛选窗口</strong>（窗口期内暂不提醒）。</item>
/// <item>窗口开启期间：外部注入按键事件（<see cref="HandleKeyPress"/>）；命中配置热键 → 置为"已确认"。</item>
/// <item>后续采样判定：窗口开启且未超时并已确认 → 触发提醒并关闭窗口、进入冷却。</item>
/// <item>窗口开启但超时未确认 → 不提醒，关闭窗口（不进入冷却，下次超阈值可再次开窗）。</item>
/// </list>
/// 热键输入由外部注入（便于单元测试），本类只做纯逻辑判定，不接触键盘钩子。
/// </remarks>
public sealed class HotkeyConfirmDecisionSource : IAlertDecisionSource
{
    /// <summary>判定源标识（用于仲裁模块的优先级排序与配置映射）。</summary>
    public const string SourceId = "hotkey-confirm";

    private DateTime _windowOpenUntilUtc = DateTime.MinValue;
    private DateTime _nextAlertTimeUtc = DateTime.MinValue;
    private bool _confirmed;

    /// <param name="threshold">开启筛选窗口的分贝阈值（显示刻度 0..150）。</param>
    /// <param name="hotkey">筛选窗口内要求命中的热键。</param>
    /// <param name="windowTimeout">筛选窗口的开启时长；为 null 时使用默认 10 秒。</param>
    /// <param name="cooldown">提醒冷却时间；为 null 时无冷却。</param>
    public HotkeyConfirmDecisionSource(
        double threshold = 120.0,
        HotkeyDefinition hotkey = default,
        TimeSpan? windowTimeout = null,
        TimeSpan? cooldown = null)
    {
        Threshold = threshold;
        Hotkey = hotkey;
        WindowTimeout = windowTimeout ?? TimeSpan.FromSeconds(10);
        Cooldown = cooldown ?? TimeSpan.Zero;
    }

    /// <inheritdoc />
    public string Id => SourceId;

    /// <inheritdoc />
    public string DisplayName => "热键确认";

    /// <inheritdoc />
    public string Description => "超过阈值时开启筛选窗口，窗口内按下热键才提醒。";

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <summary>开启筛选窗口的分贝阈值（显示刻度 0..150）。</summary>
    public double Threshold { get; set; }

    /// <summary>筛选窗口内要求命中的热键。未配置时为无效热键，窗口内永远无法命中。</summary>
    public HotkeyDefinition Hotkey { get; set; }

    /// <summary>筛选窗口的开启时长。</summary>
    public TimeSpan WindowTimeout { get; set; }

    /// <inheritdoc />
    public TimeSpan Cooldown { get; set; }

    /// <summary>当前筛选窗口是否开启（供组件显示提醒状态）。</summary>
    public bool IsWindowOpen { get; private set; }

    /// <summary>当前是否处于"超过阈值"状态（供组件显示提醒状态）。</summary>
    public bool IsTriggerActive { get; private set; }

    /// <summary>下一次允许提醒的时间（UTC）。</summary>
    public DateTime NextAlertTimeUtc => _nextAlertTimeUtc;

    /// <inheritdoc />
    public bool IsCoolingDown(DateTime nowUtc) =>
        IsEnabled && _nextAlertTimeUtc != DateTime.MinValue && nowUtc < _nextAlertTimeUtc;

    /// <summary>
    /// 外部注入一次按键事件（后台钩子或测试直接调用）。
    /// 仅当筛选窗口开启且命中配置热键时置为"已确认"。
    /// </summary>
    /// <returns>本次按键是否命中并确认了当前窗口。</returns>
    public bool HandleKeyPress(string? keyName, HotkeyModifiers modifiers, DateTime nowUtc)
    {
        if (!IsEnabled || !IsWindowOpen || nowUtc > _windowOpenUntilUtc) return false;

        if (Hotkey.Matches(keyName, modifiers))
        {
            _confirmed = true;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public AlertDecision Decide(AlertContext context, DateTime nowUtc)
    {
        if (!IsEnabled)
        {
            // 未启用时清空内部状态
            Reset();
            return default;
        }

        var isActive = context.AverageDecibel > Threshold;
        IsTriggerActive = isActive;

        var windowOpen = IsWindowOpen;

        // 窗口未开 && 超阈值 && 冷却已过 → 开窗（不立即提醒，等待热键确认）
        if (!windowOpen)
        {
            if (isActive && nowUtc >= _nextAlertTimeUtc && Hotkey.IsValid)
            {
                IsWindowOpen = true;
                _confirmed = false;
                _windowOpenUntilUtc = nowUtc + (WindowTimeout > TimeSpan.Zero ? WindowTimeout : TimeSpan.Zero);
            }

            return new AlertDecision(false, isActive);
        }

        // 窗口已开但超时 → 关窗（本次不提醒，不进入冷却）
        if (nowUtc > _windowOpenUntilUtc)
        {
            IsWindowOpen = false;
            _confirmed = false;
            return new AlertDecision(false, isActive);
        }

        // 窗口开启且未超时：只有已命中热键才提醒
        if (!_confirmed) return new AlertDecision(false, isActive);

        // 命中确认 → 触发提醒，关窗并进入冷却
        IsWindowOpen = false;
        _confirmed = false;
        _nextAlertTimeUtc = nowUtc + (Cooldown > TimeSpan.Zero ? Cooldown : TimeSpan.Zero);
        return new AlertDecision(true, isActive);
    }

    private void Reset()
    {
        IsWindowOpen = false;
        IsTriggerActive = false;
        _confirmed = false;
        _windowOpenUntilUtc = DateTime.MinValue;
        _nextAlertTimeUtc = DateTime.MinValue;
    }
}