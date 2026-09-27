using System;
using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class IndicatorStateResolverTests
{
    private const double Threshold = 120.0;

    /// <summary>构造一次解析输入（默认：有采样、已启用判定源、条件均不成立、数值低于阈值）。</summary>
    private static IndicatorInputs Inputs(
        bool hasSample = true,
        bool anySourceEnabled = true,
        bool windowOpen = false,
        bool triggerActive = false,
        bool coolingDown = false,
        double average = 100.0,
        double shortAverage = 100.0,
        double threshold = Threshold,
        TimeSpan? windowRemaining = null,
        TimeSpan? windowTimeout = null) => new()
        {
            HasSample = hasSample,
            AnySourceEnabled = anySourceEnabled,
            AnyWindowOpen = windowOpen,
            AnyTriggerActive = triggerActive,
            AnyCoolingDown = coolingDown,
            AverageDecibel = average,
            ShortWindowAverageDecibel = shortAverage,
            Threshold = threshold,
            HotkeyWindowRemaining = windowRemaining ?? TimeSpan.Zero,
            HotkeyWindowTimeout = windowTimeout ?? TimeSpan.Zero,
        };

    // ---- 合并规则：两源全开的全部组合 ----

    [Theory]
    [InlineData(true, false, false)]  // 窗口开 → 最高优先级
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]    // 窗口开 + 另一源冷却中：窗口必须压制冷却
    public void ResolveCandidate_Should_ReturnAwaitingHotkey_When_WindowOpen(bool windowOpen, bool trigger, bool cooling)
    {
        var state = IndicatorStateResolver.ResolveCandidate(Inputs(windowOpen: windowOpen, triggerActive: trigger, coolingDown: cooling));

        Assert.Equal(IndicatorState.AwaitingHotkey, state);
    }

    [Fact]
    public void ResolveCandidate_Should_ReturnAlerting_When_TriggerActiveWithoutWindow()
    {
        // 超阈值优先于冷却：A 源正在触发（未冷却）时，即使 B 源处于冷却，也不能把 A 的实心方盖成空心方。
        // 运行时已把"处于冷却期的源"从 AnyTriggerActive 中排除，因此这里的组合只可能来自不同源。
        var state = IndicatorStateResolver.ResolveCandidate(Inputs(triggerActive: true, coolingDown: true));

        Assert.Equal(IndicatorState.Alerting, state);
    }

    [Fact]
    public void ResolveCandidate_Should_ReturnCoolingDown_When_OnlyCooling()
    {
        var state = IndicatorStateResolver.ResolveCandidate(Inputs(coolingDown: true));

        Assert.Equal(IndicatorState.CoolingDown, state);
    }

    [Fact]
    public void ResolveCandidate_Should_ReturnNormal_When_NothingActive()
    {
        var state = IndicatorStateResolver.ResolveCandidate(Inputs());

        Assert.Equal(IndicatorState.Normal, state);
    }

    [Fact]
    public void ResolveCandidate_Should_ReturnNoData_When_NoSample()
    {
        var state = IndicatorStateResolver.ResolveCandidate(Inputs(hasSample: false));

        Assert.Equal(IndicatorState.NoData, state);
    }

    [Fact]
    public void ResolveCandidate_Should_ReturnNoData_When_NoSourceEnabled()
    {
        // 未启用任何判定源时不显示形状（与现状一致），即使采样正常
        var state = IndicatorStateResolver.ResolveCandidate(Inputs(anySourceEnabled: false));

        Assert.Equal(IndicatorState.NoData, state);
    }

    // ---- 时间滞回 ----

    [Fact]
    public void Update_Should_ApplyUpgradeImmediately()
    {
        var resolver = new IndicatorStateResolver();
        var t0 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);

        var snapshot = resolver.Update(Inputs(triggerActive: true), t0);

        Assert.Equal(IndicatorState.Alerting, snapshot.State);
        Assert.True(snapshot.StateChanged);
    }

    [Fact]
    public void Update_Should_HoldDowngrade_UntilNewStatePersistsOneSecond()
    {
        var resolver = new IndicatorStateResolver();
        var t0 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        resolver.Update(Inputs(triggerActive: true), t0);

        var held = resolver.Update(Inputs(), t0.AddMilliseconds(300));
        Assert.Equal(IndicatorState.Alerting, held.State);
        Assert.False(held.StateChanged);

        var stillHeld = resolver.Update(Inputs(), t0.AddMilliseconds(1200));
        Assert.Equal(IndicatorState.Alerting, stillHeld.State);

        var released = resolver.Update(Inputs(), t0.AddMilliseconds(1400));
        Assert.Equal(IndicatorState.Normal, released.State);
        Assert.True(released.StateChanged);
    }

    [Fact]
    public void Update_Should_CancelPendingDowngrade_When_HigherStateReturns()
    {
        var resolver = new IndicatorStateResolver();
        var t0 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        resolver.Update(Inputs(triggerActive: true), t0);

        // 第一次回落：起算待决
        resolver.Update(Inputs(), t0.AddMilliseconds(300));

        // 又回到高优先级态：待决必须被取消
        var backToAlerting = resolver.Update(Inputs(triggerActive: true), t0.AddMilliseconds(500));
        Assert.Equal(IndicatorState.Alerting, backToAlerting.State);

        // 再次回落：从此刻重新起算，1 秒不足以降级
        var held = resolver.Update(Inputs(), t0.AddMilliseconds(1600));
        Assert.Equal(IndicatorState.Alerting, held.State);

        var released = resolver.Update(Inputs(), t0.AddMilliseconds(2700));
        Assert.Equal(IndicatorState.Normal, released.State);
    }

    [Fact]
    public void Update_Should_ReleaseDowngradeByRealElapsedTime_NotByCallCount()
    {
        // 采样间隔可配：调用次数多但真实经过时间不足时不得降级
        var resolver = new IndicatorStateResolver();
        var t0 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        resolver.Update(Inputs(triggerActive: true), t0);

        for (var i = 0; i < 20; i++)
        {
            resolver.Update(Inputs(), t0.AddMilliseconds(10 * (i + 1)));
        }

        Assert.Equal(IndicatorState.Alerting, resolver.State);
        Assert.Equal(IndicatorState.Normal, resolver.Update(Inputs(), t0.AddMilliseconds(1050)).State);
    }

    // ---- 数字颜色判据：阈值迟滞 ----

    [Fact]
    public void Update_Should_NotReportAbove_When_ValueEqualsThreshold()
    {
        var resolver = new IndicatorStateResolver();

        var snapshot = resolver.Update(Inputs(average: Threshold), DateTime.UtcNow);

        Assert.Equal(IndicatorValueState.Below, snapshot.ValueState);
    }

    [Fact]
    public void Update_Should_TurnValueStateAbove_When_ValueExceedsThreshold()
    {
        var resolver = new IndicatorStateResolver();

        var snapshot = resolver.Update(Inputs(average: Threshold + 1), DateTime.UtcNow);

        Assert.Equal(IndicatorValueState.Above, snapshot.ValueState);
    }

    [Fact]
    public void Update_Should_KeepAbove_Until_ValueDropsBelowExitThreshold()
    {
        var resolver = new IndicatorStateResolver();
        var now = DateTime.UtcNow;

        resolver.Update(Inputs(average: Threshold + 1), now);

        // 迟滞带内（退出阈值 = 120 - 2 = 118）不回落
        Assert.Equal(IndicatorValueState.Above, resolver.Update(Inputs(average: Threshold - 0.5), now).ValueState);
        Assert.Equal(IndicatorValueState.Above, resolver.Update(Inputs(average: Threshold - 2), now).ValueState);

        // 越过 118 才回落
        Assert.Equal(IndicatorValueState.Below, resolver.Update(Inputs(average: Threshold - 2.1), now).ValueState);
    }

    [Fact]
    public void Update_Should_NotHysteresis_When_BandDisabled()
    {
        var resolver = new IndicatorStateResolver(0.0);
        var now = DateTime.UtcNow;

        resolver.Update(Inputs(average: Threshold + 1), now);

        Assert.Equal(IndicatorValueState.Below, resolver.Update(Inputs(average: Threshold - 0.5), now).ValueState);
    }

    // ---- 数字颜色判据：冷却期改用 1 秒短窗口平均 ----

    [Fact]
    public void Update_Should_UseShortWindowAverage_When_CoolingDown()
    {
        var resolver = new IndicatorStateResolver();

        // 冷却期：长平均仍高但短窗口已回落 → 绿
        var snapshot = resolver.Update(
            Inputs(coolingDown: true, average: 140.0, shortAverage: 100.0),
            DateTime.UtcNow);

        Assert.Equal(IndicatorState.CoolingDown, snapshot.State);
        Assert.Equal(IndicatorValueState.Below, snapshot.ValueState);
    }

    [Fact]
    public void Update_Should_ReportAbove_When_CoolingDownAndShortWindowStillHigh()
    {
        var resolver = new IndicatorStateResolver();

        var snapshot = resolver.Update(
            Inputs(coolingDown: true, average: 100.0, shortAverage: 140.0),
            DateTime.UtcNow);

        Assert.Equal(IndicatorState.CoolingDown, snapshot.State);
        Assert.Equal(IndicatorValueState.Above, snapshot.ValueState);
    }

    [Fact]
    public void Update_Should_UseLongAverage_When_NotCoolingDown()
    {
        var resolver = new IndicatorStateResolver();

        // 非冷却期：只用长平均，短窗口值不参与
        var snapshot = resolver.Update(
            Inputs(triggerActive: true, average: 130.0, shortAverage: 100.0),
            DateTime.UtcNow);

        Assert.Equal(IndicatorValueState.Above, snapshot.ValueState);
    }

    [Fact]
    public void Update_Should_ReturnUnknownValueState_When_NoSample()
    {
        var resolver = new IndicatorStateResolver();

        var snapshot = resolver.Update(Inputs(hasSample: false, average: 140.0), DateTime.UtcNow);

        Assert.Equal(IndicatorState.NoData, snapshot.State);
        Assert.Equal(IndicatorValueState.Unknown, snapshot.ValueState);
    }

    // ---- 呼吸时机 ----

    [Theory]
    [InlineData(10, 3.0, false)]   // 窗口 10 秒 → 提前量 min(3, 2.5) = 2.5 秒
    [InlineData(10, 2.5, true)]
    [InlineData(10, 2.0, true)]
    [InlineData(20, 3.0, true)]    // 窗口 20 秒 → 提前量 min(3, 5) = 3 秒
    [InlineData(20, 3.1, false)]
    [InlineData(40, 3.0, true)]
    [InlineData(40, 3.1, false)]
    public void ShouldBreathe_Should_DependOnRemainingTime(int windowSeconds, double remainingSeconds, bool expected)
    {
        var breathing = IndicatorStateResolver.ShouldBreathe(
            IndicatorState.AwaitingHotkey,
            TimeSpan.FromSeconds(remainingSeconds),
            TimeSpan.FromSeconds(windowSeconds));

        Assert.Equal(expected, breathing);
    }

    [Fact]
    public void ShouldBreathe_Should_ReturnFalse_When_StateIsNotAwaitingHotkey()
    {
        Assert.False(IndicatorStateResolver.ShouldBreathe(IndicatorState.Normal, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        Assert.False(IndicatorStateResolver.ShouldBreathe(IndicatorState.Alerting, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        Assert.False(IndicatorStateResolver.ShouldBreathe(IndicatorState.CoolingDown, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
    }

    // ---- Changed 标志与 Reset ----

    [Fact]
    public void Update_Should_ReportChanged_When_ValueStateChangesOnly()
    {
        var resolver = new IndicatorStateResolver();
        var now = DateTime.UtcNow;
        var steady = resolver.Update(Inputs(average: 100.0), now);
        Assert.True(steady.Changed);

        // 状态不变、数字判据变红 → Changed 仍为真
        var red = resolver.Update(Inputs(average: 130.0), now);
        Assert.False(red.StateChanged);
        Assert.True(red.Changed);

        // 状态与判据都不变 → Changed 为假
        var same = resolver.Update(Inputs(average: 130.0), now);
        Assert.False(same.Changed);
    }

    [Fact]
    public void Reset_Should_ClearStateAndHysteresis()
    {
        var resolver = new IndicatorStateResolver();
        var now = DateTime.UtcNow;
        resolver.Update(Inputs(triggerActive: true, average: 130.0), now);

        resolver.Reset();
        Assert.Equal(IndicatorState.NoData, resolver.State);

        // Reset 后下一次解析必须报 Changed（组件需强制刷新视觉），且迟滞标记已清空
        var snapshot = resolver.Update(Inputs(average: 100.0), now);
        Assert.Equal(IndicatorState.Normal, snapshot.State);
        Assert.True(snapshot.Changed);
        Assert.Equal(IndicatorValueState.Below, snapshot.ValueState);
    }

    [Fact]
    public void Reset_Should_NotHoldDowngrade()
    {
        // 设备切换后残留的红色必须立即消失，不经过 1 秒降级保持
        var resolver = new IndicatorStateResolver();
        var t0 = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
        resolver.Update(Inputs(triggerActive: true), t0);

        resolver.Reset();

        Assert.Equal(IndicatorState.Normal, resolver.Update(Inputs(), t0.AddMilliseconds(200)).State);
    }
}