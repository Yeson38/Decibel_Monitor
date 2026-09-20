using System;
using System.Collections.Generic;
using System.Linq;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 仲裁结果：本次是否提醒，以及是由哪个判定源触发的。
/// </summary>
/// <param name="ShouldAlert">是否发出提醒。</param>
/// <param name="TriggerSourceId">触发提醒的判定源标识；<see cref="ShouldAlert"/> 为 false 时可为 null。</param>
public readonly record struct CoordinatorDecision(bool ShouldAlert, string? TriggerSourceId);

/// <summary>
/// 仲裁模块：收集所有启用判定源的判定结果，按优先级仲裁，并归口发出提醒通知。
/// </summary>
/// <remarks>
/// 职责与边界：
/// <list type="bullet">
/// <item><description>持有判定源集合与优先级配置（<see cref="SourcePriorityOrder"/>），<strong>优先级只在此配置</strong>，判定源自身不带优先级。</description></item>
/// <item><description>每个采样周期调用所有启用判定源的 <see cref="IAlertDecisionSource.Decide"/>，取优先级最高的触发结果。</description></item>
/// <item><description>裁定需要提醒时，通过 <see cref="NotifyRequested"/> 事件唯一出口发出通知——<strong>判定源不接触通知</strong>。</description></item>
/// </list>
/// <exception cref="InvalidOperationException">构造时未提供任何判定源。</exception>
/// </remarks>
public sealed class AlertDecisionCoordinator
{
    private IAlertDecisionSource[] _sources;

    /// <summary>
    /// 需要发出提醒时触发。参数为触发提醒的判定源标识（供宿主映射到具体通知内容）。
    /// 判定源本身不订阅此事件，由宿主运行时服务接入真实通知提供方。
    /// </summary>
    public event Action<string>? NotifyRequested;

    /// <param name="sources">全部判定源（含未启用的，启用状态在判定时读取）。</param>
    /// <param name="sourcePriorityOrder">判定源优先级顺序（按 Id 排序，靠前的优先级更高）。为空时按传入顺序。</param>
    public AlertDecisionCoordinator(IEnumerable<IAlertDecisionSource> sources, IReadOnlyList<string>? sourcePriorityOrder = null)
    {
        var list = (sources ?? throw new ArgumentNullException(nameof(sources))).ToArray();
        if (list.Length == 0) throw new InvalidOperationException("至少需要一个判定源。");

        _sources = Sort(list, sourcePriorityOrder);
    }

    /// <summary>按优先级排序后的全部判定源（含未启用的）。</summary>
    public IReadOnlyList<IAlertDecisionSource> Sources => _sources;

    /// <summary>
    /// 更新判定源优先级顺序（供设置页调整后生效）。未在顺序中出现的判定源按原顺序追加到末尾。
    /// </summary>
    /// <param name="sourcePriorityOrder">判定源优先级顺序（按 Id 排序，靠前的优先级更高）。为空时保持原顺序。</param>
    public void ApplyPriorityOrder(IReadOnlyList<string>? sourcePriorityOrder)
    {
        if (sourcePriorityOrder is not { Count: > 0 }) return;
        _sources = Sort(_sources, sourcePriorityOrder);
    }

    /// <summary>
    /// 按优先级顺序重排判定源；缺失条目不代表要禁用，仅按原顺序追加到末尾。
    /// </summary>
    private static IAlertDecisionSource[] Sort(
        IReadOnlyList<IAlertDecisionSource> sources,
        IReadOnlyList<string>? sourcePriorityOrder)
    {
        if (sourcePriorityOrder is not { Count: > 0 }) return sources.ToArray();

        var ordered = new List<IAlertDecisionSource>(sources.Count);
        foreach (var id in sourcePriorityOrder)
        {
            var matched = sources.FirstOrDefault(s => s.Id == id);
            if (matched is not null && ordered.All(o => o.Id != matched.Id))
            {
                ordered.Add(matched);
            }
        }

        // 仅追加未被优先级顺序覆盖的判定源（保持稳定顺序）
        foreach (var s in sources)
        {
            if (ordered.All(o => o.Id != s.Id))
            {
                ordered.Add(s);
            }
        }

        return ordered.ToArray();
    }

    /// <summary>
    /// 依据当前上下文仲裁是否提醒：调用所有启用判定源（各自推进内部状态机），取优先级最高的触发结果。
    /// 若需提醒，触发 <see cref="NotifyRequested"/> 事件并返回对应结果。
    /// </summary>
    public CoordinatorDecision Decide(AlertContext context, DateTime nowUtc)
    {
        // 必须先让所有判定源各自完成一次判定：判定源内部有冷却/筛选窗口等状态机，
        // 提前返回会让低优先级判定源错过本周期，导致其窗口无法按时关闭。
        string? triggerSourceId = null;
        foreach (var source in _sources)
        {
            var decision = source.Decide(context, nowUtc);
            if (decision.ShouldAlert && triggerSourceId is null)
            {
                triggerSourceId = source.Id;
            }
        }

        if (triggerSourceId is null) return new CoordinatorDecision(false, null);

        NotifyRequested?.Invoke(triggerSourceId);
        return new CoordinatorDecision(true, triggerSourceId);
    }
}