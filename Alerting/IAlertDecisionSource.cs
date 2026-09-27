using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 一次判定的输入上下文（由运行时编排服务在每个采样周期构造）。
/// </summary>
/// <param name="CurrentDecibel">当前瞬时映射分贝值（显示刻度 0..150）。</param>
/// <param name="AverageDecibel">滑动窗口平均映射分贝值（显示刻度 0..150），窗口内无样本时等于当前值。</param>
public readonly record struct AlertContext(double CurrentDecibel, double AverageDecibel);

/// <summary>
/// 单个判定源的判定结果。
/// </summary>
/// <param name="ShouldAlert">本次判定是否需要发出提醒。</param>
/// <param name="IsTriggerActive">当前是否处于"触发条件成立"状态（供组件显示提醒状态）。</param>
public readonly record struct AlertDecision(bool ShouldAlert, bool IsTriggerActive);

/// <summary>
/// 判定源：一种"判断方式"。
/// </summary>
/// <remarks>
/// 设计约束：
/// <list type="bullet">
/// <item><description>判定源只负责"判断何时该提醒"，<strong>不接触通知</strong>——通知由
/// <see cref="AlertDecisionCoordinator"/> 统一发出。</description></item>
/// <item><description>判定源自带状态（冷却、筛选窗口等），实现需保证 <see cref="Decide"/> 可在每个采样周期被反复调用。</description></item>
/// <item><description>判定源设置项只有：启用开关、冷却时间、专属参数；<strong>不含优先级</strong>（优先级归仲裁模块）。</description></item>
/// </list>
/// </remarks>
public interface IAlertDecisionSource
{
    /// <summary>判定源标识（用于优先级排序与配置映射，须稳定且唯一）。</summary>
    string Id { get; }

    /// <summary>判定源显示名（设置页展示）。</summary>
    string DisplayName { get; }

    /// <summary>判定源简要说明（设置页优先级列表展示）。</summary>
    string Description { get; }

    /// <summary>是否启用该判定源。</summary>
    bool IsEnabled { get; }

    /// <summary>该判定源自身的提醒冷却时间（两次提醒之间的最小间隔）。</summary>
    TimeSpan Cooldown { get; }

    /// <summary>
    /// 当前是否处于冷却期（上次提醒后尚未超过 <see cref="Cooldown"/>，不具备再次提醒的资格）。
    /// 未启用的判定源恒为 false。
    /// </summary>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    bool IsCoolingDown(DateTime nowUtc);

    /// <summary>
    /// 依据当前上下文判定是否提醒。未启用时应返回"不提醒"并清空内部状态。
    /// </summary>
    /// <param name="context">当前判定上下文。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    AlertDecision Decide(AlertContext context, DateTime nowUtc);
}
