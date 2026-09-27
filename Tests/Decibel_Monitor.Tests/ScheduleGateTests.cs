using System;
using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class ScheduleGateTests
{
    /// <summary>测试基准时间。</summary>
    private static readonly DateTime Start = new(2026, 9, 26, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>按默认参数（课表已启用并加载、课间抑制开、保护 3 分钟）推进一次判定。</summary>
    private static bool Advance(
        ScheduleGate gate,
        SchedulePhase phase,
        DateTime nowUtc,
        bool suppressDuringBreak = true,
        int protectionMinutes = 3,
        bool isClassPlanEnabled = true,
        bool isClassPlanLoaded = true) =>
        gate.Update(phase, isClassPlanEnabled, isClassPlanLoaded, suppressDuringBreak, protectionMinutes, nowUtc);

    // ---- 课表状态旁路 ----

    [Fact]
    public void Update_Should_NotSuppress_When_ClassPlanDisabled()
    {
        var gate = new ScheduleGate();

        // 即使处于课间，课表未启用时时段信息不可信 → 不抑制
        Assert.False(Advance(gate, SchedulePhase.Breaking, Start, isClassPlanEnabled: false));
    }

    [Fact]
    public void Update_Should_NotSuppress_When_ClassPlanNotLoaded()
    {
        var gate = new ScheduleGate();

        Assert.False(Advance(gate, SchedulePhase.Breaking, Start, isClassPlanLoaded: false));
    }

    [Fact]
    public void Update_Should_NotSuppress_When_PlanDisabledDuringClassStart()
    {
        var gate = new ScheduleGate();

        Assert.False(Advance(gate, SchedulePhase.OnClass, Start, isClassPlanEnabled: false));
    }

    // ---- 课间 ----

    [Fact]
    public void Update_Should_Suppress_When_BreakingAndSwitchOn()
    {
        var gate = new ScheduleGate();

        Assert.True(Advance(gate, SchedulePhase.Breaking, Start));
    }

    [Fact]
    public void Update_Should_NotSuppress_When_BreakingButSwitchOff()
    {
        var gate = new ScheduleGate();

        Assert.False(Advance(gate, SchedulePhase.Breaking, Start, suppressDuringBreak: false));
    }

    // ---- 上课初期保护 ----

    [Fact]
    public void Update_Should_Suppress_WithinClassStartProtection()
    {
        var gate = new ScheduleGate();

        // 上升沿建立起点，随后第 1 分钟仍处于 3 分钟保护内
        Assert.True(Advance(gate, SchedulePhase.OnClass, Start));
        Assert.True(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(1)));
    }

    [Fact]
    public void Update_Should_NotSuppress_AfterClassStartProtection()
    {
        var gate = new ScheduleGate();

        Advance(gate, SchedulePhase.OnClass, Start);

        Assert.False(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(4)));
    }

    [Fact]
    public void Update_Should_NotSuppress_At_ProtectionBoundary()
    {
        var gate = new ScheduleGate();

        Advance(gate, SchedulePhase.OnClass, Start);

        // 严格小于：正好 3 分钟时保护已结束
        Assert.False(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(3)));
    }

    [Fact]
    public void Update_Should_NotSuppress_When_ProtectionDisabled()
    {
        var gate = new ScheduleGate();

        // 保护时长 0 = 关闭该保护，上课第 1 分钟就应正常判定
        Assert.False(Advance(gate, SchedulePhase.OnClass, Start, protectionMinutes: 0));
    }

    [Fact]
    public void Update_Should_Clamp_ProtectionMinutes_ToUpperBound()
    {
        var gate = new ScheduleGate();

        // 传入超过上限的值按 10 分钟处理：第 9 分钟仍保护、第 11 分钟不再保护
        Advance(gate, SchedulePhase.OnClass, Start, protectionMinutes: 99);

        Assert.True(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(9), protectionMinutes: 99));
        Assert.False(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(11), protectionMinutes: 99));
    }

    [Fact]
    public void Update_Should_TreatNegativeProtectionMinutes_AsDisabled()
    {
        var gate = new ScheduleGate();

        Assert.False(Advance(gate, SchedulePhase.OnClass, Start, protectionMinutes: -5));
    }

    // ---- 上升沿重新计时 ----

    [Fact]
    public void Update_Should_RestartProtection_On_RisingEdgeOfClass()
    {
        var gate = new ScheduleGate();

        // 第一节：保护已过
        Advance(gate, SchedulePhase.OnClass, Start);
        Assert.False(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(10)));

        // 课间：抑制，但不影响下一节课的起点
        var breakStart = Start.AddMinutes(45);
        Assert.True(Advance(gate, SchedulePhase.Breaking, breakStart));

        // 第二节上课：上升沿重新计时 → 保护重新生效
        var secondClass = breakStart.AddMinutes(10);
        Assert.True(Advance(gate, SchedulePhase.OnClass, secondClass));
        Assert.False(Advance(gate, SchedulePhase.OnClass, secondClass.AddMinutes(3)));
    }

    [Fact]
    public void Update_Should_NotRestartProtection_While_StayingOnClass()
    {
        var gate = new ScheduleGate();

        // 连续的上课 tick 不应把起点往后推：起点始终是上升沿那一刻
        Advance(gate, SchedulePhase.OnClass, Start);

        for (var i = 1; i <= 5; i++) Advance(gate, SchedulePhase.OnClass, Start.AddSeconds(i));

        Assert.False(Advance(gate, SchedulePhase.OnClass, Start.AddMinutes(3)));
    }

    // ---- 其它时段 ----

    [Fact]
    public void Update_Should_NotSuppress_When_OtherPhase()
    {
        var gate = new ScheduleGate();

        Assert.False(Advance(gate, SchedulePhase.Other, Start));
    }
}