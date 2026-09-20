using System;
using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Decibel_Monitor.Alarm;
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

    /// <summary>是否显示提醒状态点（任一判定源已启用且组件设置未关闭时显示）。</summary>
    public static readonly StyledProperty<bool> IsIndicatorVisibleProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsIndicatorVisible));

    /// <summary>提醒状态点是否为红色：红=提醒触发中（含冷却期），绿=正常。</summary>
    public static readonly StyledProperty<bool> IsIndicatorAlertProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsIndicatorAlert));

    /// <summary>筛选窗口开启时状态点的闪烁半周期（毫秒）：红灯、绿灯各持续这么久。</summary>
    private const int BlinkHalfPeriodMs = 400;

    private readonly DispatcherTimer _updateTimer;
    private readonly AlertRuntimeService? _runtimeService;
    private volatile bool _isActive;
    private volatile bool _isUpdating;

    /// <summary>
    /// 当前显示的分贝值文本（Avalonia 属性，绑定自动响应变化）。
    /// </summary>
    public string CurrentDecibelValue
    {
        get => GetValue(CurrentDecibelValueProperty);
        private set => SetValue(CurrentDecibelValueProperty, value);
    }

    /// <summary>
    /// 是否显示提醒状态点（组件设置关闭或没有启用任何判定源时为 false）。
    /// </summary>
    public bool IsIndicatorVisible
    {
        get => GetValue(IsIndicatorVisibleProperty);
        private set => SetValue(IsIndicatorVisibleProperty, value);
    }

    /// <summary>
    /// 提醒状态点是否为红色：红=提醒触发中（含冷却期），绿=正常；
    /// 筛选窗口开启时在红绿之间交替，形成闪动。
    /// </summary>
    public bool IsIndicatorAlert
    {
        get => GetValue(IsIndicatorAlertProperty);
        private set => SetValue(IsIndicatorAlertProperty, value);
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
            // 应用设置的更新频率（设置变化后下一拍即生效）；
            // 筛选窗口开启时收紧到闪烁半周期，保证红绿交替看得见
            int intervalMs = Math.Clamp(Settings?.UpdateIntervalMs ?? 200, 100, 5000);
            if (_runtimeService?.IsHotkeyWindowOpen == true) intervalMs = Math.Min(intervalMs, BlinkHalfPeriodMs);
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

            // 单一状态点：红=提醒触发中（超阈值/冷却期），绿=正常；
            // 筛选窗口开启时红绿交替闪动，与静态状态区分开
            IsIndicatorVisible = showIndicator && _runtimeService.IsAnySourceEnabled;
            IsIndicatorAlert = _runtimeService.IsHotkeyWindowOpen
                ? Environment.TickCount64 / BlinkHalfPeriodMs % 2 == 0
                : _runtimeService.IsAlertStateActive;
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