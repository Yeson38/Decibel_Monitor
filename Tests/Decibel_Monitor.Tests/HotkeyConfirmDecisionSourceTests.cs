using System;
using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class HotkeyConfirmDecisionSourceTests
{
    private const double Threshold = 100.0;
    private const string KeyName = "F5";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private static AlertContext Context(double averageDb) => new(averageDb, averageDb);

    private static HotkeyConfirmDecisionSource CreateSource(
        bool enabled = true,
        TimeSpan? cooldown = null,
        HotkeyModifiers modifiers = HotkeyModifiers.None)
        => new(Threshold, new HotkeyDefinition(KeyName, modifiers), Window, cooldown) { IsEnabled = enabled };

    // ---- 窗口开闭 ----

    [Fact]
    public void Decide_Should_NotOpenWindow_WhenDisabled()
    {
        var source = CreateSource(enabled: false);

        var decision = source.Decide(Context(150.0), Now);

        Assert.False(decision.ShouldAlert);
        Assert.False(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_NotOpenWindow_WhenBelowThreshold()
    {
        var source = CreateSource();

        var decision = source.Decide(Context(50.0), Now);

        Assert.False(decision.ShouldAlert);
        Assert.False(decision.IsTriggerActive);
        Assert.False(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_OpenWindow_WithoutAlerting_WhenOverThreshold()
    {
        var source = CreateSource();

        var decision = source.Decide(Context(120.0), Now);

        Assert.False(decision.ShouldAlert);
        Assert.True(decision.IsTriggerActive);
        Assert.True(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_NotOpenWindow_WhenHotkeyNotConfigured()
    {
        var source = new HotkeyConfirmDecisionSource(Threshold, HotkeyDefinition.None, Window) { IsEnabled = true };

        var decision = source.Decide(Context(120.0), Now);

        Assert.False(decision.ShouldAlert);
        Assert.False(source.IsWindowOpen);
    }

    // ---- 热键确认 ----

    [Fact]
    public void Decide_Should_Alert_WhenHotkeyHitInsideWindow()
    {
        var source = CreateSource();
        source.Decide(Context(120.0), Now);

        var hit = source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now.AddSeconds(1));
        var decision = source.Decide(Context(120.0), Now.AddSeconds(1));

        Assert.True(hit);
        Assert.True(decision.ShouldAlert);
        Assert.False(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_NotAlert_WhenOtherKeyPressedInsideWindow()
    {
        var source = CreateSource();
        source.Decide(Context(120.0), Now);

        var hit = source.HandleKeyPress("F6", HotkeyModifiers.None, Now.AddSeconds(1));
        var decision = source.Decide(Context(120.0), Now.AddSeconds(1));

        Assert.False(hit);
        Assert.False(decision.ShouldAlert);
        Assert.True(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_NotAlert_WhenModifiersMismatch()
    {
        var source = CreateSource(modifiers: HotkeyModifiers.Ctrl);
        source.Decide(Context(120.0), Now);

        var hit = source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now.AddSeconds(1));
        var decision = source.Decide(Context(120.0), Now.AddSeconds(1));

        Assert.False(hit);
        Assert.False(decision.ShouldAlert);
        Assert.True(source.IsWindowOpen);
    }

    [Fact]
    public void HandleKeyPress_Should_ReturnFalse_WhenWindowNotOpen()
    {
        var source = CreateSource();

        Assert.False(source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now));
    }

    // ---- 窗口超时 ----

    [Fact]
    public void WindowOpenUntilUtc_Should_ExposeDeadlineOnlyWhileWindowOpen()
    {
        // 供运行时服务计算窗口剩余时间与呼吸时机：窗口关闭后不得残留过期截止时间
        var source = CreateSource();
        Assert.Equal(DateTime.MinValue, source.WindowOpenUntilUtc);

        source.Decide(Context(120.0), Now);
        Assert.Equal(Now + Window, source.WindowOpenUntilUtc);

        source.Decide(Context(120.0), Now + Window + TimeSpan.FromMilliseconds(1));
        Assert.False(source.IsWindowOpen);
        Assert.Equal(DateTime.MinValue, source.WindowOpenUntilUtc);
    }

    [Fact]
    public void Decide_Should_CloseWindowWithoutAlerting_WhenWindowTimedOut()
    {
        var source = CreateSource();
        source.Decide(Context(120.0), Now);

        var timedOut = source.Decide(Context(120.0), Now + Window + TimeSpan.FromMilliseconds(1));

        Assert.False(timedOut.ShouldAlert);
        Assert.False(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_IgnoreKeyPress_WhenWindowTimedOut()
    {
        var source = CreateSource();
        source.Decide(Context(120.0), Now);

        var hit = source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now + Window + TimeSpan.FromSeconds(1));

        Assert.False(hit);
    }

    [Fact]
    public void Decide_Should_OpenWindowAgain_AfterTimeout()
    {
        // 超时未确认不进入冷却，仍在超阈值时可再次开窗
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.Decide(Context(120.0), Now + Window + TimeSpan.FromSeconds(1));

        source.Decide(Context(120.0), Now + Window + TimeSpan.FromSeconds(2));

        Assert.True(source.IsWindowOpen);
    }

    // ---- 冷却 ----

    [Fact]
    public void Decide_Should_NotOpenWindow_WhileCoolingDown()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now.AddSeconds(1));
        Assert.True(source.Decide(Context(120.0), Now.AddSeconds(1)).ShouldAlert);

        var cooling = source.Decide(Context(120.0), Now.AddSeconds(2));

        Assert.False(cooling.ShouldAlert);
        Assert.False(source.IsWindowOpen);
        Assert.Equal(Now.AddSeconds(1).AddMinutes(5), source.NextAlertTimeUtc);
    }

    [Fact]
    public void Decide_Should_OpenWindowAgain_AfterCooldownElapsed()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now);
        source.Decide(Context(120.0), Now);

        var afterCooldown = Now.AddMinutes(5);
        source.Decide(Context(120.0), afterCooldown);

        Assert.True(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_ResetWindow_WhenDisabled()
    {
        var source = CreateSource();
        source.Decide(Context(120.0), Now);
        Assert.True(source.IsWindowOpen);

        source.IsEnabled = false;
        source.Decide(Context(120.0), Now.AddSeconds(1));

        Assert.False(source.IsWindowOpen);
        Assert.False(source.IsTriggerActive);
    }

    [Fact]
    public void IsCoolingDown_Should_BeTrue_AfterAlert_UntilCooldownElapsed()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        Assert.False(source.IsCoolingDown(Now));

        source.Decide(Context(120.0), Now);
        Assert.False(source.IsCoolingDown(Now));

        source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now);
        source.Decide(Context(120.0), Now);

        Assert.True(source.IsCoolingDown(Now.AddMinutes(1)));
        Assert.False(source.IsCoolingDown(Now.AddMinutes(5)));
    }

    [Fact]
    public void IsCoolingDown_Should_BeFalse_WhenWindowClosedWithoutConfirming()
    {
        // 窗口超时未确认不进入冷却，状态点不应因超时保持点亮
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);

        source.Decide(Context(120.0), Now.Add(Window).AddSeconds(1));

        Assert.False(source.IsCoolingDown(Now.Add(Window).AddSeconds(1)));
    }

    // ---- 热键匹配 ----

    [Fact]
    public void Hotkey_Should_MatchKeyName_IgnoringCase()
    {
        var hotkey = new HotkeyDefinition("f5", HotkeyModifiers.None);

        Assert.True(hotkey.Matches("F5", HotkeyModifiers.None));
    }

    [Fact]
    public void Hotkey_Should_RequireExactModifiers()
    {
        var hotkey = new HotkeyDefinition(KeyName, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);

        Assert.True(hotkey.Matches(KeyName, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift));
        Assert.False(hotkey.Matches(KeyName, HotkeyModifiers.Ctrl));
        Assert.False(hotkey.Matches(KeyName, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt));
    }

    [Fact]
    public void Hotkey_Should_NotMatch_WhenKeyNameEmpty()
    {
        Assert.False(HotkeyDefinition.None.Matches(KeyName, HotkeyModifiers.None));
        Assert.False(new HotkeyDefinition(KeyName, HotkeyModifiers.None).Matches(null, HotkeyModifiers.None));
    }
}
