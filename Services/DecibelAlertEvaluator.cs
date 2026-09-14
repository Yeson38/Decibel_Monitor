using System;

namespace Decibel_Monitor.Services;

/// <summary>
/// 分贝提醒评估结果。
/// </summary>
/// <param name="IsActive">是否处于“超过阈值”提醒状态（用于组件显示提示文字）。</param>
/// <param name="ShouldNotify">本次评估是否需要发送提醒（超阈值且冷却已过）。</param>
/// <param name="NextAlertTimeUtc">下一次允许发送提醒的时间（UTC）。</param>
public readonly record struct DecibelAlertDecision(bool IsActive, bool ShouldNotify, DateTime NextAlertTimeUtc);

/// <summary>
/// 分贝提醒评估纯函数（与 UI / 宿主无关，便于单元测试）。
/// </summary>
public static class DecibelAlertEvaluator
{
    /// <summary>
    /// 评估当前分贝映射值是否触发提醒。
    /// </summary>
    /// <param name="mappedDb">当前分贝映射值（显示刻度 0..150）。</param>
    /// <param name="threshold">提醒阈值（显示刻度）。</param>
    /// <param name="enabled">是否启用提醒。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <param name="nextAlertTimeUtc">上一次记录的下一次可提醒时间（UTC）。</param>
    /// <param name="cooldownMinutes">冷却时间（分钟），最小按 1 分钟处理。</param>
    /// <remarks>阈值比较为严格大于：等于阈值不触发。</remarks>
    public static DecibelAlertDecision Evaluate(
        double mappedDb,
        double threshold,
        bool enabled,
        DateTime nowUtc,
        DateTime nextAlertTimeUtc,
        int cooldownMinutes)
    {
        var isActive = enabled && mappedDb > threshold;
        var shouldNotify = isActive && nowUtc >= nextAlertTimeUtc;
        var next = shouldNotify ? nowUtc.AddMinutes(Math.Max(1, cooldownMinutes)) : nextAlertTimeUtc;
        return new DecibelAlertDecision(isActive, shouldNotify, next);
    }
}
