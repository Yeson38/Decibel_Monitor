using System;
using System.Collections.Generic;
using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class AlertDecisionCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly AlertContext Context = new(120.0, 120.0);

    private sealed class FakeSource : IAlertDecisionSource
    {
        public FakeSource(string id, bool shouldAlert, bool enabled = true)
        {
            Id = id;
            ShouldAlert = shouldAlert;
            IsEnabled = enabled;
        }

        public string Id { get; }

        public string DisplayName => Id;

        public string Description => Id;

        public bool IsEnabled { get; set; }

        public bool ShouldAlert { get; set; }

        public bool IsCoolingDownValue { get; set; }

        public TimeSpan Cooldown => TimeSpan.Zero;

        public int DecideCount { get; private set; }

        public bool IsCoolingDown(DateTime nowUtc) => IsCoolingDownValue;

        public AlertDecision Decide(AlertContext context, DateTime nowUtc)
        {
            DecideCount++;
            return IsEnabled ? new AlertDecision(ShouldAlert, ShouldAlert) : default;
        }
    }

    [Fact]
    public void Constructor_Should_Throw_WhenNoSourceProvided()
    {
        Assert.Throws<InvalidOperationException>(() => new AlertDecisionCoordinator(Array.Empty<IAlertDecisionSource>()));
    }

    [Fact]
    public void Decide_Should_Alert_ByPriorityOrder()
    {
        var low = new FakeSource("low", shouldAlert: true);
        var high = new FakeSource("high", shouldAlert: true);
        var coordinator = new AlertDecisionCoordinator(new[] { low, high }, new[] { "high", "low" });

        var decision = coordinator.Decide(Context, Now);

        Assert.True(decision.ShouldAlert);
        Assert.Equal("high", decision.TriggerSourceId);
    }

    [Fact]
    public void Decide_Should_FallBack_ToLowerPriority_WhenHigherDoesNotAlert()
    {
        var low = new FakeSource("low", shouldAlert: true);
        var high = new FakeSource("high", shouldAlert: false);
        var coordinator = new AlertDecisionCoordinator(new[] { low, high }, new[] { "high", "low" });

        var decision = coordinator.Decide(Context, Now);

        Assert.True(decision.ShouldAlert);
        Assert.Equal("low", decision.TriggerSourceId);
    }

    [Fact]
    public void Decide_Should_EvaluateAllSources_EvenAfterOneAlerts()
    {
        // 低优先级判定源也必须在每个周期完成判定，否则其内部窗口/冷却状态机无法推进
        var low = new FakeSource("low", shouldAlert: false);
        var high = new FakeSource("high", shouldAlert: true);
        var coordinator = new AlertDecisionCoordinator(new[] { low, high }, new[] { "high", "low" });

        coordinator.Decide(Context, Now);

        Assert.Equal(1, high.DecideCount);
        Assert.Equal(1, low.DecideCount);
    }

    [Fact]
    public void Decide_Should_NotAlert_WhenNoSourceTriggers()
    {
        var coordinator = new AlertDecisionCoordinator(new[] { new FakeSource("a", shouldAlert: false) });

        var decision = coordinator.Decide(Context, Now);

        Assert.False(decision.ShouldAlert);
        Assert.Null(decision.TriggerSourceId);
    }

    [Fact]
    public void Decide_Should_IgnoreDisabledSource()
    {
        var coordinator = new AlertDecisionCoordinator(new[] { new FakeSource("a", shouldAlert: true, enabled: false) });

        var decision = coordinator.Decide(Context, Now);

        Assert.False(decision.ShouldAlert);
    }

    [Fact]
    public void Decide_Should_RaiseNotifyRequested_OnlyWhenAlerting()
    {
        var coordinator = new AlertDecisionCoordinator(new[] { new FakeSource("a", shouldAlert: true) });
        var notified = new List<string>();
        coordinator.NotifyRequested += notified.Add;

        coordinator.Decide(Context, Now);
        Assert.Equal(new[] { "a" }, notified);
    }

    [Fact]
    public void Decide_Should_NotRaiseNotifyRequested_WhenNotAlerting()
    {
        var coordinator = new AlertDecisionCoordinator(new[] { new FakeSource("a", shouldAlert: false) });
        var notified = new List<string>();
        coordinator.NotifyRequested += notified.Add;

        coordinator.Decide(Context, Now);

        Assert.Empty(notified);
    }

    [Fact]
    public void Sources_Should_AppendUnlistedSources_AfterPriorityOrder()
    {
        var a = new FakeSource("a", shouldAlert: false);
        var b = new FakeSource("b", shouldAlert: false);
        var c = new FakeSource("c", shouldAlert: false);
        var coordinator = new AlertDecisionCoordinator(new[] { a, b, c }, new[] { "c", "a" });

        Assert.Equal(new[] { "c", "a", "b" }, new[] { coordinator.Sources[0].Id, coordinator.Sources[1].Id, coordinator.Sources[2].Id });
    }

    [Fact]
    public void Sources_Should_KeepGivenOrder_WhenNoPriorityConfigured()
    {
        var a = new FakeSource("a", shouldAlert: false);
        var b = new FakeSource("b", shouldAlert: false);
        var coordinator = new AlertDecisionCoordinator(new[] { a, b });

        Assert.Equal(new[] { "a", "b" }, new[] { coordinator.Sources[0].Id, coordinator.Sources[1].Id });
    }

    [Fact]
    public void ApplyPriorityOrder_Should_ReorderSources_AndTakeEffectOnNextDecide()
    {
        var low = new FakeSource("low", shouldAlert: true);
        var high = new FakeSource("high", shouldAlert: true);
        var coordinator = new AlertDecisionCoordinator(new[] { low, high }, new[] { "high", "low" });

        coordinator.ApplyPriorityOrder(new[] { "low", "high" });

        Assert.Equal(new[] { "low", "high" }, new[] { coordinator.Sources[0].Id, coordinator.Sources[1].Id });
        Assert.Equal("low", coordinator.Decide(Context, Now).TriggerSourceId);
    }

    [Fact]
    public void ApplyPriorityOrder_Should_IgnoreEmptyOrder()
    {
        var a = new FakeSource("a", shouldAlert: false);
        var b = new FakeSource("b", shouldAlert: false);
        var coordinator = new AlertDecisionCoordinator(new[] { a, b }, new[] { "b", "a" });

        coordinator.ApplyPriorityOrder(Array.Empty<string>());
        coordinator.ApplyPriorityOrder(null);

        Assert.Equal(new[] { "b", "a" }, new[] { coordinator.Sources[0].Id, coordinator.Sources[1].Id });
    }

    [Fact]
    public void ApplyPriorityOrder_Should_AppendUnlistedSources()
    {
        var a = new FakeSource("a", shouldAlert: false);
        var b = new FakeSource("b", shouldAlert: false);
        var c = new FakeSource("c", shouldAlert: false);
        var coordinator = new AlertDecisionCoordinator(new[] { a, b, c });

        coordinator.ApplyPriorityOrder(new[] { "b" });

        Assert.Equal(new[] { "b", "a", "c" }, new[] { coordinator.Sources[0].Id, coordinator.Sources[1].Id, coordinator.Sources[2].Id });
    }
}
