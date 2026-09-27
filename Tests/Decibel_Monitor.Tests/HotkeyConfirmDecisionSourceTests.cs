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
    public void Decide_Should_Alert_WhenConfirmedKeyConsumedAfterDeadline()
    {
        // 采样拍与窗口截止时间不对齐：按键本身落在窗口内（HandleKeyPress 放行，_confirmed 置真），
        // 但消费这次确认的那一拍已经越过截止时间。
        // 这类确认必须兑现：否则用户明明按了键，却既不提醒、又白进一次冷却。
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);

        var hit = source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now + Window - TimeSpan.FromMilliseconds(1));
        var decisionAt = Now + Window + TimeSpan.FromMilliseconds(400);
        var decision = source.Decide(Context(120.0), decisionAt);

        Assert.True(hit);
        Assert.True(decision.ShouldAlert);
        Assert.False(source.IsWindowOpen);
        Assert.Equal(decisionAt.AddMinutes(5), source.NextAlertTimeUtc);
    }

    // ---- 闸门抑制：窗口作废 ----

    [Fact]
    public void CancelWindow_Should_DropWindowAndConfirmation_WithoutWritingCooldown()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now + TimeSpan.FromSeconds(1));

        source.CancelWindow();

        Assert.False(source.IsWindowOpen);
        Assert.Equal(DateTime.MinValue, source.WindowOpenUntilUtc);
        Assert.Equal(DateTime.MinValue, source.NextAlertTimeUtc);
        Assert.False(source.IsCoolingDown(Now + TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void CancelWindow_Should_BeNoOp_WhenWindowClosed()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));

        source.CancelWindow();

        Assert.False(source.IsWindowOpen);
        Assert.Equal(DateTime.MinValue, source.NextAlertTimeUtc);
    }

    [Fact]
    public void Decide_Should_NotAlert_AfterCancelledConfirmation()
    {
        // 闸门期间作废掉的确认不能在闸门解除后被兑现，否则"课间按的键，上课后才响"
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.HandleKeyPress(KeyName, HotkeyModifiers.None, Now + TimeSpan.FromSeconds(1));
        source.CancelWindow();

        var decision = source.Decide(Context(120.0), Now + TimeSpan.FromSeconds(2));

        Assert.False(decision.ShouldAlert);
    }

    [Fact]
    public void Decide_Should_ReopenFullWindow_AfterGateLifted()
    {
        // 作废不写冷却：闸门解除后仍超阈值 → 立刻重开一个完整时长的窗口
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.CancelWindow();

        var reopenAt = Now + TimeSpan.FromSeconds(30);
        source.Decide(Context(120.0), reopenAt);

        Assert.True(source.IsWindowOpen);
        Assert.Equal(reopenAt + Window, source.WindowOpenUntilUtc);
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
    public void Decide_Should_EnterCooldown_WhenWindowTimedOut()
    {
        // 超时未确认同样视为"已经给过一次机会"：关窗并写入冷却
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);

        var timeoutAt = Now + Window + TimeSpan.FromSeconds(1);
        var timedOut = source.Decide(Context(120.0), timeoutAt);

        Assert.False(timedOut.ShouldAlert);
        Assert.False(source.IsWindowOpen);
        Assert.Equal(timeoutAt.AddMinutes(5), source.NextAlertTimeUtc);
        Assert.True(source.IsCoolingDown(timeoutAt.AddSeconds(1)));
    }

    [Fact]
    public void Decide_Should_NotOpenWindowAgain_WhileTimeoutCooldownActive()
    {
        // 超时后的冷却期内即使仍超阈值也不开窗，否则三角会立即重新常亮
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        source.Decide(Context(120.0), Now + Window + TimeSpan.FromSeconds(1));

        source.Decide(Context(120.0), Now + Window + TimeSpan.FromSeconds(2));

        Assert.False(source.IsWindowOpen);
    }

    [Fact]
    public void Decide_Should_OpenWindowAgain_AfterTimeoutCooldownElapsed()
    {
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);
        var timeoutAt = Now + Window + TimeSpan.FromSeconds(1);
        source.Decide(Context(120.0), timeoutAt);

        source.Decide(Context(120.0), timeoutAt.AddMinutes(5));

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
    public void IsCoolingDown_Should_BeTrue_WhenWindowClosedWithoutConfirming()
    {
        // 超时未确认同样进入冷却：状态点在超时后转为冷却空心方，而不是一直停在三角
        var source = CreateSource(cooldown: TimeSpan.FromMinutes(5));
        source.Decide(Context(120.0), Now);

        var timeoutAt = Now.Add(Window).AddSeconds(1);
        source.Decide(Context(120.0), timeoutAt);

        Assert.True(source.IsCoolingDown(timeoutAt));
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
