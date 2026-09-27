using System;
using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Decibel_Monitor.Alarm;
using Decibel_Monitor.Alerting;
using DecibelComponentSettings = Decibel_Monitor.Models.ComponentSettings.DecibelComponentSettings;

namespace Decibel_Monitor.Controls.Components;

[ComponentInfo(
    "3540a8df-963c-45e9-a42d-8a55824994d5",
    "分贝值",
    "\uEB88",
    "在主界面上显示麦克风分贝值。"
)]
public partial class DecibelComponent : ComponentBase<DecibelComponentSettings>
{
    /// <summary>当前显示的分贝值文本。</summary>
    public static readonly StyledProperty<string> CurrentDecibelValueProperty =
        AvaloniaProperty.Register<DecibelComponent, string>(nameof(CurrentDecibelValue), "N/A");

    /// <summary>是否显示提醒状态点（组件设置未关闭且存在有效采样时显示）。</summary>
    public static readonly StyledProperty<bool> IsIndicatorVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsIndicatorVisible));

    /// <summary>常态是否显示绿圆。</summary>
    public static readonly StyledProperty<bool> IsCircleVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsCircleVisible));

    /// <summary>正在超阈值时是否显示红实心方。</summary>
    public static readonly StyledProperty<bool> IsSquareFilledVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsSquareFilledVisible));

    /// <summary>提醒后的冷却期是否显示红空心方。</summary>
    public static readonly StyledProperty<bool> IsSquareHollowVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsSquareHollowVisible));

    /// <summary>待热键确认时是否显示橙三角。</summary>
    public static readonly StyledProperty<bool> IsTriangleVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsTriangleVisible));

    /// <summary>三角是否呼吸（筛选窗口剩余时间已进入收紧区间）。</summary>
    public static readonly StyledProperty<bool> IsBreathingProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsBreathing));

    /// <summary>数字是否使用"判据成立"颜色（红）。</summary>
    public static readonly StyledProperty<bool> IsValueAboveThresholdProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsValueAboveThreshold));

    /// <summary>数字是否使用"判据不成立"颜色（绿）。</summary>
    public static readonly StyledProperty<bool> IsValueWithinThresholdProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsValueWithinThreshold));

    private readonly DispatcherTimer _updateTimer;
    private readonly AlertRuntimeService? _runtimeService;
    private volatile bool _isActive;
    private volatile bool _isUpdating;

    /// <summary>本实例最近一次写入视觉属性的形状状态（自持比对用，见 <see cref="UpdateTimer_Tick"/>）。</summary>
    private IndicatorState _appliedState = IndicatorState.NoData;

    /// <summary>本实例最近一次写入视觉属性的数字颜色判据。</summary>
    private IndicatorValueState _appliedValueState = IndicatorValueState.Unknown;

    /// <summary>
    /// 当前显示的分贝值文本（Avalonia 属性，绑定自动响应变化）。
    /// </summary>
    public string CurrentDecibelValue
    {
        get => GetValue(CurrentDecibelValueProperty);
        private set => SetValue(CurrentDecibelValueProperty, value);
    }

    /// <summary>
    /// 是否显示提醒状态点（组件设置关闭或没有有效采样时为 false）。
    /// </summary>
    public bool IsIndicatorVisible
    {
        get => GetValue(IsIndicatorVisibleProperty);
        private set => SetValue(IsIndicatorVisibleProperty, value);
    }

    /// <summary>常态是否显示绿圆。</summary>
    public bool IsCircleVisible
    {
        get => GetValue(IsCircleVisibleProperty);
        private set => SetValue(IsCircleVisibleProperty, value);
    }

    /// <summary>正在超阈值时是否显示红实心方。</summary>
    public bool IsSquareFilledVisible
    {
        get => GetValue(IsSquareFilledVisibleProperty);
        private set => SetValue(IsSquareFilledVisibleProperty, value);
    }

    /// <summary>提醒后的冷却期是否显示红空心方。</summary>
    public bool IsSquareHollowVisible
    {
        get => GetValue(IsSquareHollowVisibleProperty);
        private set => SetValue(IsSquareHollowVisibleProperty, value);
    }

    /// <summary>待热键确认时是否显示橙三角。</summary>
    public bool IsTriangleVisible
    {
        get => GetValue(IsTriangleVisibleProperty);
        private set => SetValue(IsTriangleVisibleProperty, value);
    }

    /// <summary>三角是否呼吸（筛选窗口即将超时）。</summary>
    public bool IsBreathing
    {
        get => GetValue(IsBreathingProperty);
        private set => SetValue(IsBreathingProperty, value);
    }

    /// <summary>数字是否使用"判据成立"颜色（红）。</summary>
    public bool IsValueAboveThreshold
    {
        get => GetValue(IsValueAboveThresholdProperty);
        private set => SetValue(IsValueAboveThresholdProperty, value);
    }

    /// <summary>数字是否使用"判据不成立"颜色（绿）。</summary>
    public bool IsValueWithinThreshold
    {
        get => GetValue(IsValueWithinThresholdProperty);
        private set => SetValue(IsValueWithinThresholdProperty, value);
    }

    public DecibelComponent()
    {
        InitializeComponent();

        // 从宿主 DI 容器获取提醒运行时服务（单例）：采样与判定都在该服务的节拍中完成，
        // 组件只读取其状态用于显示。动态解析而非构造期缓存：插件加载早期组件可能先于
        // 通知提供方（IHostedService）创建。
        _runtimeService = IAppHost.Host?.Services.GetService(typeof(AlertRuntimeService)) as AlertRuntimeService;

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        // 定时器不在这里启动：组件生命周期改由视觉树决定（见 OnAttachedToVisualTree /
        // OnDetachedFromVisualTree）。宿主通过 DI 根容器解析瞬态组件且从不调用释放方法，
        // 若在构造期启动定时器，已启动的 DispatcherTimer 会被 Dispatcher 强引用，
        // Tick 订阅闭包会永久钉住组件实例，导致组件重建后旧实例无法回收。
    }

    // 定时器随视觉树的附着/分离启停：宿主从 DI 根容器解析瞬态组件且从不调用释放方法，
    // 不能依赖释放方法兜底，改为跟随视觉树生命周期，组件离开视觉树后即退订
    // Tick 并停止定时器，避免已启动的 DispatcherTimer 通过 Tick 闭包钉住实例。
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 可重入守卫：附着/分离可能成对多次发生，重复 Start 会导致重复计时
        if (_isActive) return;
        _isActive = true;
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (!_isActive) return;
        _isActive = false;
        _updateTimer.Tick -= UpdateTimer_Tick;
        _updateTimer.Stop();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // 节流守卫：附着/分离的边界情形下可能重入
        if (_isUpdating) return;
        _isUpdating = true;
        try
        {
            // 应用设置的更新频率（设置变化后下一拍即生效）
            int intervalMs = Math.Clamp(Settings?.UpdateIntervalMs ?? 200, 100, 5000);
            if (Math.Abs(_updateTimer.Interval.TotalMilliseconds - intervalMs) > 0.5)
            {
                _updateTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            }

            if (_runtimeService is null)
            {
                CurrentDecibelValue = "无采样服务";
                IsIndicatorVisible = false;
                return;
            }

            // "提醒状态点"开关只影响组件上是否显示状态点，不影响提醒链路
            bool showIndicator = Settings?.ShowStatusIndicator ?? true;
            bool showPrefix = Settings?.ShowDecibelPrefix ?? true;

            CurrentDecibelValue = _runtimeService.HasSample
                ? (showPrefix ? $"分贝: {_runtimeService.CurrentDecibel:F1}" : $"{_runtimeService.CurrentDecibel:F1}")
                : "N/A";

            var state = _runtimeService.IndicatorState;
            var valueState = _runtimeService.IndicatorValueState;

            // 无采样或未启用任何判定源时（解析结果 NoData）不显示任何形状
            IsIndicatorVisible = showIndicator && state != IndicatorState.NoData;

            // 呼吸的起止由时间推进而非状态变化触发，不参与下方的自持比对，必须每拍读取
            IsBreathing = _runtimeService.IsIndicatorBreathing;

            // 形状与数字颜色是"离散状态"：只在与本实例已应用的组合不同时才重设。
            // 这里不能依赖服务侧的"状态已变化"标志 —— 它由采样判定逐拍产出，宽度只有一个采样拍（200 ms），
            // 组件节拍（默认 500 ms）可能整拍落在脉冲之外；一旦漏读，形状会一直冻结在旧值，
            // 直到下一次被捕获的脉冲为止（表现为"热键窗口已开却仍显示空心方"，且此时按键仍能提醒）。
            // 改为自持最近一次应用值比对后，最坏情况只是滞后一个组件节拍，不会长期冻结。
            if (state == _appliedState && valueState == _appliedValueState) return;

            IsCircleVisible = state == IndicatorState.Normal;
            IsSquareFilledVisible = state == IndicatorState.Alerting;
            IsSquareHollowVisible = state == IndicatorState.CoolingDown;
            IsTriangleVisible = state == IndicatorState.AwaitingHotkey;
            IsValueAboveThreshold = valueState == IndicatorValueState.Above;
            IsValueWithinThreshold = valueState == IndicatorValueState.Below;
            _appliedState = state;
            _appliedValueState = valueState;
        }
        catch (Exception)
        {
            CurrentDecibelValue = "读取失败";
        }
        finally
        {
            _isUpdating = false;
        }
    }
}