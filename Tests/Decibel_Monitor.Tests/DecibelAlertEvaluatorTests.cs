using System;
using Decibel_Monitor.Services;

namespace Decibel_Monitor.Tests;

public class DecibelAlertEvaluatorTests
{
    private const double Threshold = 100.0;

    // ---- 激活判定 ----

    [Fact]
    public void Evaluate_Should_NotActivate_WhenDisabled()
    {
        var decision = DecibelAlertEvaluator.Evaluate(120.0, Threshold, false, DateTime.UtcNow, DateTime.MinValue, 5);

        Assert.False(decision.IsActive);
        Assert.False(decision.ShouldNotify);
        Assert.Equal(DateTime.MinValue, decision.NextAlertTimeUtc);
    }

    [Fact]
    public void Evaluate_Should_NotActivate_WhenBelowThreshold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var decision = DecibelAlertEvaluator.Evaluate(99.9, Threshold, true, now, DateTime.MinValue, 5);

        Assert.False(decision.IsActive);
        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public void Evaluate_Should_NotActivate_WhenEqualToThreshold()
    {
        // 边界：严格大于才算超阈值
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var decision = DecibelAlertEvaluator.Evaluate(Threshold, Threshold, true, now, DateTime.MinValue, 5);

        Assert.False(decision.IsActive);
        Assert.False(decision.ShouldNotify);
    }

    // ---- 冷却控制 ----

    [Fact]
    public void Evaluate_Should_Notify_OnFirstOverThreshold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var decision = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, now, DateTime.MinValue, 5);

        Assert.True(decision.IsActive);
        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void Evaluate_Should_NotNotify_WhileCoolingDown()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = now.AddMinutes(5);
        var decision = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, now, next, 5);

        Assert.True(decision.IsActive);      // 仍处于超阈值状态（组件继续显示提示文字）
        Assert.False(decision.ShouldNotify); // 但冷却未过，不重复提醒
        Assert.Equal(next, decision.NextAlertTimeUtc);
    }

    [Fact]
    public void Evaluate_Should_Notify_WhenCooldownJustElapsed()
    {
        var next = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc);
        var decision = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, next, next, 5);

        Assert.True(decision.IsActive);
        Assert.True(decision.ShouldNotify);
    }

    [Fact]
    public void Evaluate_Should_SetNextAlertTime_ByCooldown()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var decision = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, now, DateTime.MinValue, 3);

        Assert.Equal(now.AddMinutes(3), decision.NextAlertTimeUtc);
    }

    [Fact]
    public void Evaluate_Should_UseMinimumOneMinuteCooldown()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var zero = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, now, DateTime.MinValue, 0);
        var negative = DecibelAlertEvaluator.Evaluate(120.0, Threshold, true, now, DateTime.MinValue, -10);

        Assert.Equal(now.AddMinutes(1), zero.NextAlertTimeUtc);
        Assert.Equal(now.AddMinutes(1), negative.NextAlertTimeUtc);
    }

    [Fact]
    public void Evaluate_Should_KeepNextAlertTime_WhenNotActive()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var next = now.AddMinutes(5);
        var decision = DecibelAlertEvaluator.Evaluate(50.0, Threshold, true, now, next, 5);

        Assert.False(decision.IsActive);
        Assert.Equal(next, decision.NextAlertTimeUtc);
    }
}
