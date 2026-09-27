using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 判定源①：到阈值自动提醒。
/// 当前（窗口平均）分贝值超过阈值且自身冷却已过时，直接判定为需要提醒。
/// </summary>
public sealed class AutoThresholdDecisionSource : IAlertDecisionSource
{
    /// <summary>判定源标识（用于仲裁模块的优先级排序与配置映射）。</summary>
    public const string SourceId = "auto-threshold";

    private DateTime _nextAlertTimeUtc = DateTime.MinValue;

    /// <param name="threshold">提醒阈值（显示刻度 0..150）。</param>
    /// <param name="cooldown">提醒冷却时间；为 null 时表示无冷却。</param>
    public AutoThresholdDecisionSource(double threshold = 120.0, TimeSpan? cooldown = null)
    {
        Threshold = threshold;
        Cooldown = cooldown ?? TimeSpan.Zero;
    }

    /// <inheritdoc />
    public string Id => SourceId;

    /// <inheritdoc />
    public string DisplayName => "自动提醒";

    /// <inheritdoc />
    public string Description => "平均分贝超过阈值时立即提醒。";

    /// <inheritdoc />
    public bool IsEnabled { get; set; }

    /// <summary>提醒阈值（显示刻度 0..150）。</summary>
    public double Threshold { get; set; }

    /// <inheritdoc />
    public TimeSpan Cooldown { get; set; }

    /// <summary>下一次允许提醒的时间（UTC）；<see cref="DateTime.MinValue"/> 表示从未提醒过。</summary>
    public DateTime NextAlertTimeUtc => _nextAlertTimeUtc;

    /// <summary>当前是否处于"超过阈值"状态（供组件显示提醒状态）。</summary>
    public bool IsTriggerActive { get; private set; }

    /// <inheritdoc />
    public bool IsCoolingDown(DateTime nowUtc) =>
        IsEnabled && _nextAlertTimeUtc != DateTime.MinValue && nowUtc < _nextAlertTimeUtc;

    /// <inheritdoc />
    public AlertDecision Decide(AlertContext context, DateTime nowUtc)
    {
        if (!IsEnabled)
        {
            // 未启用时清空状态：再次启用后不会沿用旧冷却
            IsTriggerActive = false;
            _nextAlertTimeUtc = DateTime.MinValue;
            return default;
        }

        // 阈值比较为严格大于：等于阈值不算超阈值
        var isActive = context.AverageDecibel > Threshold;
        IsTriggerActive = isActive;
        if (!isActive) return new AlertDecision(false, false);

        if (nowUtc < _nextAlertTimeUtc) return new AlertDecision(false, true);

        _nextAlertTimeUtc = nowUtc + EffectiveCooldown();
        return new AlertDecision(true, true);
    }

    /// <summary>生效冷却时间：负值按无冷却处理。</summary>
    private TimeSpan EffectiveCooldown() => Cooldown > TimeSpan.Zero ? Cooldown : TimeSpan.Zero;
}
