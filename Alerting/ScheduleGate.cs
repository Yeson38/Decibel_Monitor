using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 闸门关心的时段类别，由宿主 <c>TimeState</c> 映射而来（只保留需要区分的情形）。
/// </summary>
public enum SchedulePhase
{
    /// <summary>其它时段（无时间点、准备上课、放学等），不抑制。</summary>
    Other,

    /// <summary>上课。</summary>
    OnClass,

    /// <summary>课间休息。</summary>
    Breaking,
}

/// <summary>
/// 时段闸门：依据课表时段（课间休息、上课初期）判定本拍是否应抑制提醒。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>纯逻辑、可单测：由运行时服务在每个采样节拍推进，<strong>不自持定时器、不订阅宿主事件</strong>，
/// 也不引用宿主类型（宿主时段由调用方映射为 <see cref="SchedulePhase"/>）。</description></item>
/// <item><description>抑制只决定"要不要推进判定源"。调用方在抑制时须<strong>跳过</strong> <c>Decide</c> 调用，
/// 否则判定源内部仍会写入冷却时间——课间的一次误报会吃掉整段冷却，反而让上课时该提醒而不提醒。</description></item>
/// <item><description>上课起点取 <see cref="SchedulePhase.OnClass"/> 的上升沿（以插件首次观察到进入上课状态为准，
/// 而非课表里的真实开课时间），并用真实时间相减计时（采样节拍可配，不能按采样次数累加）。
/// 因此插件在课中启动/重启时会从该时刻重新计时，偏保守。</description></item>
/// <item><description>课表未启用或未加载时整体旁路，行为回到未接入闸门之前。</description></item>
/// </list>
/// </remarks>
public sealed class ScheduleGate
{
    /// <summary>上课初期保护时长的上限（分钟）。</summary>
    public const int MaxClassStartProtectionMinutes = 10;

    private SchedulePhase _lastPhase = SchedulePhase.Other;
    private DateTime _classStartUtc = DateTime.MinValue;

    /// <summary>
    /// 推进一次判定并返回本拍是否应抑制提醒。
    /// </summary>
    /// <param name="phase">当前时段类别。</param>
    /// <param name="isClassPlanEnabled">宿主是否启用课表。</param>
    /// <param name="isClassPlanLoaded">宿主是否已加载课表。</param>
    /// <param name="suppressDuringBreak">课间休息期间是否抑制提醒。</param>
    /// <param name="classStartProtectionMinutes">上课初期保护时长（分钟），按 0..<see cref="MaxClassStartProtectionMinutes"/> 夹取，0 表示关闭。</param>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <returns>本拍是否应抑制提醒。</returns>
    public bool Update(
        SchedulePhase phase,
        bool isClassPlanEnabled,
        bool isClassPlanLoaded,
        bool suppressDuringBreak,
        int classStartProtectionMinutes,
        DateTime nowUtc)
    {
        // 上课上升沿：每次进入 OnClass 都重新计时（课间 → 上课的转换会重置保护起点）
        if (phase == SchedulePhase.OnClass && _lastPhase != SchedulePhase.OnClass)
        {
            _classStartUtc = nowUtc;
        }

        _lastPhase = phase;

        // 课表未启用或未加载：时段信息不可信，闸门整体旁路
        if (!isClassPlanEnabled || !isClassPlanLoaded) return false;

        if (suppressDuringBreak && phase == SchedulePhase.Breaking) return true;

        var protectionMinutes = Math.Clamp(classStartProtectionMinutes, 0, MaxClassStartProtectionMinutes);
        if (protectionMinutes > 0 &&
            phase == SchedulePhase.OnClass &&
            nowUtc - _classStartUtc < TimeSpan.FromMinutes(protectionMinutes))
        {
            return true;
        }

        return false;
    }
}