using System;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 提醒状态点的离散状态（组件据此选择形状与填充，不再自行解释布尔条件）。
/// </summary>
public enum IndicatorState
{
    /// <summary>无采样，或未启用任何判定源：不显示任何形状。</summary>
    NoData,

    /// <summary>有采样且各判定条件均不成立。</summary>
    Normal,

    /// <summary>
    /// 任一判定源当前处于"超过阈值且未冷却"状态（提醒正由仲裁模块发出）。
    /// 提醒与冷却在同一拍写入，因此该态实际只在判定源冷却时间为 0 时可见。
    /// </summary>
    Alerting,

    /// <summary>任一判定源处于提醒后的冷却期（提醒已经发生过）。</summary>
    CoolingDown,

    /// <summary>热键筛选窗口开启，等待按键确认——唯一需要用户动手的状态。</summary>
    AwaitingHotkey
}

/// <summary>
/// 数字颜色的判据结果：当前值是否已满足告警条件（与 <see cref="IndicatorState"/> 相互独立）。
/// </summary>
public enum IndicatorValueState
{
    /// <summary>无采样 / 未启用任何判定源：数字使用默认色。</summary>
    Unknown,

    /// <summary>判据不成立：数字为绿。</summary>
    Below,

    /// <summary>判据成立：数字为红。</summary>
    Above
}

/// <summary>
/// 一次状态解析的输入（由运行时服务在每个采样周期构造）。
/// </summary>
/// <remarks>
/// 输入均为"已压平"的布尔量与数值，不携带判定源身份——状态点不表达源身份，
/// 两个源同时启用时按"是否需要用户动手"取最高者。
/// </remarks>
public readonly record struct IndicatorInputs
{
    /// <summary>是否已取得过至少一次采样。</summary>
    public bool HasSample { get; init; }

    /// <summary>是否至少启用了一个判定源。</summary>
    public bool AnySourceEnabled { get; init; }

    /// <summary>任一判定源的热键筛选窗口是否开启。</summary>
    public bool AnyWindowOpen { get; init; }

    /// <summary>
    /// 任一判定源是否处于"超过阈值<strong>且未处于冷却期</strong>"状态（判定条件成立、提醒即将或正在发出）。
    /// 已进入冷却的源不算数——否则低阈值用法下冷却期内会一直显示"正在超阈值"，"刚提醒过"这一事实会被盖掉。
    /// </summary>
    public bool AnyTriggerActive { get; init; }

    /// <summary>任一判定源是否处于提醒后的冷却期。</summary>
    public bool AnyCoolingDown { get; init; }

    /// <summary>判定用的滑动窗口平均分贝（显示刻度 0..150）。</summary>
    public double AverageDecibel { get; init; }

    /// <summary>1 秒短窗口平均分贝（显示刻度 0..150），冷却期内作为数字颜色判据。</summary>
    public double ShortWindowAverageDecibel { get; init; }

    /// <summary>
    /// 数字颜色判据的阈值（显示刻度 0..150）。多个判定源同时启用时应传入其中<strong>最小者</strong>：
    /// 各源判据共用同一个数值，因此"任一源超阈"等价于"数值超过最小阈值"。
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>热键筛选窗口的剩余时间（无窗口时为 <see cref="TimeSpan.Zero"/>）。</summary>
    public TimeSpan HotkeyWindowRemaining { get; init; }

    /// <summary>热键筛选窗口的开启时长（用于换算呼吸提前量）。</summary>
    public TimeSpan HotkeyWindowTimeout { get; init; }
}

/// <summary>
/// 一次状态解析的结果。
/// </summary>
/// <param name="State">合并后的提醒状态（已套用时间滞回）。</param>
/// <param name="ValueState">数字颜色的判据结果（已套用阈值迟滞）。</param>
/// <param name="StateChanged">状态与上一次解析结果相比是否发生变化。</param>
/// <param name="Changed">状态或数字判据任一发生变化（组件据此跳过无变化的视觉重设）。</param>
/// <param name="IsBreathing">当前是否应呼吸（仅窗口即将超时时为真）。</param>
public readonly record struct IndicatorSnapshot(
    IndicatorState State,
    IndicatorValueState ValueState,
    bool StateChanged,
    bool Changed,
    bool IsBreathing);

/// <summary>
/// 提醒状态点解析器：把各判定源的压平状态合并为单一 <see cref="IndicatorState"/>，
/// 并给出数字颜色判据与呼吸时机。
/// </summary>
/// <remarks>
/// 设计约束（纯逻辑，不依赖 Avalonia / NAudio / Windows，可在任意平台单测）：
/// <list type="bullet">
/// <item><description><strong>合并规则按"是否需要用户动手"排序</strong>：窗口 &gt; 超阈值 &gt; 冷却 &gt; 正常 &gt; 无数据。
/// 窗口必须压制冷却——两个源同时启用时自动源的冷却可达 1800 秒，若按严重度排序会把"该按键"盖掉。
/// <see cref="IndicatorInputs.AnyTriggerActive"/> 的输入已排除处于冷却期的源，因此<strong>冷却期的形状恒为空心方</strong>：
/// 超阈值当拍即发出提醒并进入冷却，此后形状只表达"提醒已发生过"，"现在是否还在吵"交给数字颜色。</description></item>
/// <item><description><strong>状态不携带源身份</strong>：单点无法表达 2 源 × 4 态，不做并排图标。</description></item>
/// <item><description><strong>阈值迟滞（施密特触发）</strong>只作用于数字颜色判据，用于消除判据在阈值附近的抖动引起的颜色闪变；
/// 判定源自身的触发逻辑保持不变，因此不影响提醒行为。</description></item>
/// <item><description><strong>时间滞回</strong>：升级立即生效，降级需新态持续 <see cref="DowngradeHoldDuration"/>。
/// 用于消除"两个源交替报警"造成的合并结果抖动。</description></item>
/// <item><description><strong>时间基准</strong>：全部计时使用传入的 UTC 时间戳相减（真实经过时间），
/// 不按采样次数累加——采样间隔可配，按次数计会让"1 秒"的语义随配置漂移。</description></item>
/// </list>
/// </remarks>
public sealed class IndicatorStateResolver
{
    /// <summary>
    /// 降级保持时长：切换到更低优先级的态需新态持续该时长才生效（升级不受限制，立即生效）。
    /// </summary>
    public static readonly TimeSpan DowngradeHoldDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 数字颜色判据的迟滞带宽（显示刻度单位，1 单位 = 1 dB）。
    /// </summary>
    /// <remarks>
    /// 参照 BigScreenClock 的 <c>HysteresisFactor = 0.8</c>（RMS 线性域，等价 <c>20·log10(0.8) ≈ -1.9 dB</c>）。
    /// 本插件的阈值以显示刻度存储（<c>displayDb = 150 + dBFS</c>，与 dB 为 1:1 的绝对量），
    /// 因此换算为绝对差：<c>退出阈值 = 进入阈值 − 2</c>，<strong>不能直接搬用 0.8 的乘法系数</strong>。
    /// </remarks>
    public const double HysteresisBandDb = 2.0;

    /// <summary>呼吸提前量的上限：剩余时间进入该值后才开始呼吸。</summary>
    public static readonly TimeSpan MaxBreathLeadTime = TimeSpan.FromSeconds(3);

    private readonly double _hysteresisBandDb;

    private IndicatorState _state = IndicatorState.NoData;
    private IndicatorValueState _valueState = IndicatorValueState.Unknown;
    private bool _valueAboveThreshold;
    private bool _hasSnapshot;
    private IndicatorState _pendingState = IndicatorState.NoData;
    private DateTime _pendingSinceUtc = DateTime.MinValue;

    /// <param name="hysteresisBandDb">数字判据的迟滞带宽（显示刻度单位）；为负值或 0 时表示不迟滞。</param>
    public IndicatorStateResolver(double hysteresisBandDb = HysteresisBandDb)
    {
        _hysteresisBandDb = hysteresisBandDb > 0.0 ? hysteresisBandDb : 0.0;
    }

    /// <summary>当前状态（最近一次解析的结果）。</summary>
    public IndicatorState State => _state;

    /// <summary>
    /// 合并规则本体：按"是否需要用户动手"排序取最高者，<strong>不含任何滞回</strong>。
    /// </summary>
    /// <remarks>
    /// 顺序：① 任一源窗口开启 → <see cref="IndicatorState.AwaitingHotkey"/>；
    /// ② 任一源超阈值<strong>且未冷却</strong> → <see cref="IndicatorState.Alerting"/>
    /// （判定源超阈值当拍即由仲裁模块发出提醒并写入冷却，因此该态只在冷却时间为 0 时可见）；
    /// ③ 任一源冷却中 → <see cref="IndicatorState.CoolingDown"/>；
    /// ④ 有采样且至少启用一个判定源 → <see cref="IndicatorState.Normal"/>；
    /// ⑤ 否则 → <see cref="IndicatorState.NoData"/>（无采样或未启用任何判定源时都不显示形状）。
    /// </remarks>
    public static IndicatorState ResolveCandidate(in IndicatorInputs inputs)
    {
        if (inputs.AnyWindowOpen) return IndicatorState.AwaitingHotkey;
        if (inputs.AnyTriggerActive) return IndicatorState.Alerting;
        if (inputs.AnyCoolingDown) return IndicatorState.CoolingDown;
        if (inputs.HasSample && inputs.AnySourceEnabled) return IndicatorState.Normal;
        return IndicatorState.NoData;
    }

    /// <summary>
    /// 是否应呼吸：仅窗口开启且剩余时间进入 <c>min(3 秒, 窗口时长 / 4)</c> 时为真。
    /// 窗口开启期间其余时间静置，低阈值用法下窗口长期开启也不会持续闪烁。
    /// </summary>
    /// <param name="state">当前状态。</param>
    /// <param name="windowRemaining">窗口剩余时间。</param>
    /// <param name="windowTimeout">窗口开启时长。</param>
    public static bool ShouldBreathe(IndicatorState state, TimeSpan windowRemaining, TimeSpan windowTimeout)
    {
        if (state != IndicatorState.AwaitingHotkey) return false;

        var fraction = windowTimeout > TimeSpan.Zero
            ? TimeSpan.FromTicks(windowTimeout.Ticks / 4)
            : TimeSpan.Zero;
        var lead = fraction < MaxBreathLeadTime ? fraction : MaxBreathLeadTime;
        var remaining = windowRemaining > TimeSpan.Zero ? windowRemaining : TimeSpan.Zero;
        return remaining <= lead;
    }

    /// <summary>
    /// 用当前输入解析一次状态。
    /// </summary>
    /// <param name="inputs">当前输入。</param>
    /// <param name="nowUtc">当前时间（UTC），用于时间滞回计时。</param>
    public IndicatorSnapshot Update(in IndicatorInputs inputs, DateTime nowUtc)
    {
        var candidate = ResolveCandidate(inputs);
        var state = ApplyDowngradeHold(candidate, nowUtc);

        // 数字颜色判据：冷却期内改用 1 秒短窗口平均（冷却期不会再触发提醒，不存在"红了却不提醒"的错位；
        // 继续用判定用的长平均窗口会因滞后而在已经安静后仍显示红色）；其余情形与触发判定同源。
        var value = state == IndicatorState.CoolingDown ? inputs.ShortWindowAverageDecibel : inputs.AverageDecibel;
        var valueState = ResolveValueState(inputs.HasSample && inputs.AnySourceEnabled, value, inputs.Threshold);

        var stateChanged = !_hasSnapshot || state != _state;
        var valueChanged = !_hasSnapshot || valueState != _valueState;
        var changed = stateChanged || valueChanged;

        _hasSnapshot = true;
        _state = state;
        _valueState = valueState;

        var breathing = ShouldBreathe(state, inputs.HotkeyWindowRemaining, inputs.HotkeyWindowTimeout);
        return new IndicatorSnapshot(state, valueState, stateChanged, changed, breathing);
    }

    /// <summary>
    /// 清空全部内部状态（设备切换、采样重启时调用，避免残留上一次的红色与迟滞标记）。
    /// </summary>
    public void Reset()
    {
        _state = IndicatorState.NoData;
        _valueState = IndicatorValueState.Unknown;
        _valueAboveThreshold = false;
        _hasSnapshot = false;
        _pendingState = IndicatorState.NoData;
        _pendingSinceUtc = DateTime.MinValue;
    }

    /// <summary>数字判据：进入用名义阈值，退出用"名义阈值 − 迟滞带宽"（施密特触发）。</summary>
    private IndicatorValueState ResolveValueState(bool hasValue, double value, double threshold)
    {
        if (!hasValue) return IndicatorValueState.Unknown;

        var exitThreshold = threshold - _hysteresisBandDb;

        if (_valueAboveThreshold)
        {
            // 与进入时的严格比较对称：必须低于退出阈值才回落
            if (value < exitThreshold) _valueAboveThreshold = false;
        }
        else if (value > threshold)
        {
            _valueAboveThreshold = true;
        }

        return _valueAboveThreshold ? IndicatorValueState.Above : IndicatorValueState.Below;
    }

    /// <summary>
    /// 时间滞回：升级（含同级）立即生效；降级需新态持续 <see cref="DowngradeHoldDuration"/>，
    /// 期间若新态又变回更高的态则取消待决。
    /// </summary>
    private IndicatorState ApplyDowngradeHold(IndicatorState candidate, DateTime nowUtc)
    {
        if (Priority(candidate) >= Priority(_state))
        {
            ClearPending();
            return candidate;
        }

        if (_pendingState != candidate)
        {
            _pendingState = candidate;
            _pendingSinceUtc = nowUtc;
        }

        if (nowUtc - _pendingSinceUtc < DowngradeHoldDuration) return _state;

        ClearPending();
        return candidate;
    }

    private void ClearPending()
    {
        _pendingState = IndicatorState.NoData;
        _pendingSinceUtc = DateTime.MinValue;
    }

    /// <summary>状态优先级：数值越大越"需要用户动手"。</summary>
    private static int Priority(IndicatorState state) => state switch
    {
        IndicatorState.AwaitingHotkey => 4,
        IndicatorState.Alerting => 3,
        IndicatorState.CoolingDown => 2,
        IndicatorState.Normal => 1,
        _ => 0,
    };
}