using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Enums;
using Decibel_Monitor.Alerting;
using Decibel_Monitor.Measurement;
using Decibel_Monitor.Models;
using Decibel_Monitor.Services;
using Microsoft.Extensions.Hosting;

namespace Decibel_Monitor.Alarm;

/// <summary>
/// 提醒运行时编排服务：按固定节拍采样 → 构造判定上下文 → 交由仲裁模块裁定 →
/// 裁定提醒时接入通知提供方，并进入 3 秒防自激（暂停分贝采样判定）。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>采样独立于组件视觉树：组件只读取本服务的最新状态用于显示，提醒链路不依赖组件是否显示。</description></item>
/// <item><description>设置（各判定源的启用/阈值/冷却/专属参数、平均窗口、优先级顺序）在每个节拍从插件全局设置同步到判定源，
/// 设置页改动下一拍即生效。</description></item>
/// <item><description>通知的唯一出口是仲裁模块的 <see cref="AlertDecisionCoordinator.NotifyRequested"/> 事件；
/// 本服务把它接到现有 <see cref="DecibelNotificationProvider"/>，强调通知与语音由宿主通知系统负责，
/// <strong>不新增通知/语音开关</strong>。</description></item>
/// <item><description>提醒状态点由 <see cref="IndicatorStateResolver"/> 解析：本服务只负责压平各判定源状态、
/// 维护判定用的平均窗口与冷却期判据所用的 1 秒短窗口，状态含义与滞回规则全部归解析器（纯逻辑、可单测）。</description></item>
/// <item><description><strong>时段闸门</strong>：课间休息与每节课开头若干分钟内不推进判定源（<see cref="ScheduleGate"/>，
/// 依赖宿主课表状态）。这些时段的误报既不发出通知、也不占用冷却；状态点仍照常显示当前是否超阈值。</description></item>
/// </list>
/// </remarks>
public sealed class AlertRuntimeService : IHostedService, IDisposable
{
    /// <summary>采样与判定节拍。</summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>防自激时长：执行提醒后暂停分贝采样判定。</summary>
    private static readonly TimeSpan AntiSelfKickDelay = TimeSpan.FromSeconds(3);

    /// <summary>平均窗口允许的样本数上限（防止窗口时长配置过大时无界占用内存）。</summary>
    private const int MaxWindowSamples = 2000;

    /// <summary>冷却期数字判据所用的短窗口时长（固定 1 秒，与判定用的平均窗口相互独立）。</summary>
    private static readonly TimeSpan ShortWindowDuration = TimeSpan.FromSeconds(1);

    private readonly AudioPeakMeter _audioPeakMeter;
    private readonly DecibelMonitorSettingsService? _settingsService;
    private readonly AlertDecisionCoordinator _coordinator;
    private readonly AutoThresholdDecisionSource _autoSource;
    private readonly HotkeyConfirmDecisionSource _hotkeySource;
    private readonly List<float> _window = new();
    private readonly Queue<(DateTime TimestampUtc, float Value)> _shortWindow = new();
    private readonly IndicatorStateResolver _indicatorResolver = new();
    private readonly ScheduleGate _scheduleGate = new();
    private readonly DispatcherTimer _timer;
    private DateTime _pauseUntilUtc = DateTime.MinValue;
    private string[]? _appliedPriorityOrder;
    private string? _lastDeviceDescription;
    private ILessonsService? _lessonsService;
    private bool _disposed;

    /// <summary>最新一次采样的分贝映射值（显示刻度 0..150）。</summary>
    public double CurrentDecibel { get; private set; }

    /// <summary>当前是否存在可用的默认捕获设备并已取得采样（否则组件显示占位文本与无形状状态）。</summary>
    public bool HasSample { get; private set; }

    /// <summary>提醒状态点的当前状态（组件据此选择形状与填充）。</summary>
    public IndicatorState IndicatorState { get; private set; } = IndicatorState.NoData;

    /// <summary>数字颜色的判据结果：当前值是否已满足告警条件。</summary>
    public IndicatorValueState IndicatorValueState { get; private set; } = IndicatorValueState.Unknown;

    /// <summary>筛选窗口剩余时间进入呼吸阈值后为真（组件据此让三角呼吸）。</summary>
    public bool IsIndicatorBreathing { get; private set; }

    /// <summary>本次解析中状态或数字判据是否发生变化（组件据此跳过无变化的视觉重设）。</summary>
    public bool IndicatorStateChanged { get; private set; }

    /// <summary>是否至少启用了一个判定源（决定组件是否显示提醒状态点）。</summary>
    public bool IsAnySourceEnabled => _autoSource.IsEnabled || _hotkeySource.IsEnabled;

    /// <param name="audioPeakMeter">共享的麦克风峰值采样服务。</param>
    /// <param name="settingsService">插件全局设置服务（可空；缺省时使用判定源内置默认参数）。</param>
    /// <param name="coordinator">仲裁模块（自持判定源集合与优先级配置）。</param>
    /// <param name="autoSource">自动提醒判定源（同步其设置）。</param>
    /// <param name="hotkeySource">热键确认判定源（同步其设置，并暴露筛选窗口状态）。</param>
    public AlertRuntimeService(
        AudioPeakMeter audioPeakMeter,
        DecibelMonitorSettingsService? settingsService,
        AlertDecisionCoordinator coordinator,
        AutoThresholdDecisionSource autoSource,
        HotkeyConfirmDecisionSource hotkeySource)
    {
        _audioPeakMeter = audioPeakMeter ?? throw new ArgumentNullException(nameof(audioPeakMeter));
        _settingsService = settingsService;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _autoSource = autoSource ?? throw new ArgumentNullException(nameof(autoSource));
        _hotkeySource = hotkeySource ?? throw new ArgumentNullException(nameof(hotkeySource));

        _coordinator.NotifyRequested += OnNotifyRequested;

        _timer = new DispatcherTimer { Interval = SampleInterval };
        _timer.Tick += Timer_Tick;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 仲裁模块裁定需要提醒时的唯一出口：接入现有通知提供方发出强调通知。
    /// </summary>
    private void OnNotifyRequested(string sourceId)
    {
        DecibelNotificationProvider.Instance?.Notify();
    }

    // 异步 Tick：全程保持在 DispatcherTimer 绑定的 UI 线程上下文（不就地切线程），
    // 与热键确认封送回 UI 线程的处理保持一致，避免判定源状态被并发修改。
    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        try
        {
            ApplySettings();

            // 防自激：执行提醒后 3 秒内暂停采样与判定
            if (DateTime.UtcNow < _pauseUntilUtc) return;

            var nowUtc = DateTime.UtcNow;

            // 默认捕获设备发生变化（含被拔出/禁用后为 null）时清空解析器状态与短窗口，
            // 避免残留上一次的红色与迟滞标记
            var deviceDescription = _audioPeakMeter.GetDefaultCaptureDeviceDescription();
            if (deviceDescription != _lastDeviceDescription)
            {
                _lastDeviceDescription = deviceDescription;
                _indicatorResolver.Reset();
                _shortWindow.Clear();
            }

            // 无默认捕获设备时视为无采样：状态点不显示任何形状、数字显示占位文本，
            // 不再伪装成"绿色正常"（仍需解析一次，否则组件读到的是上一次的状态）
            if (deviceDescription is null)
            {
                HasSample = false;
                UpdateIndicator(nowUtc, average: 0.0, shortAverage: 0.0, scheduleGateSuppressed: false);
                return;
            }

            var linear = await _audioPeakMeter.GetDefaultDevicePeakLinearAsync();
            var magnification = _settingsService?.Settings.Magnification ?? 1.0;
            var mapped = DecibelCalculator.LinearToDisplayDb(linear, magnification);
            CurrentDecibel = mapped;
            HasSample = true;

            // 维护平均分贝滑动窗口（容量由平均窗口时长与采样节拍换算，超出即丢弃最旧样本）
            var capacity = GetWindowCapacity();
            _window.Add((float)mapped);
            while (_window.Count > capacity) _window.RemoveAt(0);
            var average = AverageVolumeAlertEvaluator.CalculateAverage(_window);

            // 维护 1 秒短窗口平均（供冷却期数字判据使用，与判定用的平均窗口相互独立）：
            // 先入队当前样本，再按真实时间戳剔除 1 秒之前的样本
            _shortWindow.Enqueue((nowUtc, (float)mapped));
            var shortAverage = GetShortWindowAverage(nowUtc, mapped);

            // 时段闸门：课间与上课初期抑制提醒。闸门生效时必须跳过 Decide 调用——
            // Decide 一旦被调用就会写入冷却时间，课间的一次误报会吃掉整段冷却，
            // 导致进入上课后该提醒而不提醒（比不提醒更严重）。
            var scheduleGateSuppressed = EvaluateScheduleGate(nowUtc);

            var decision = default(CoordinatorDecision);
            if (!scheduleGateSuppressed)
            {
                decision = _coordinator.Decide(new AlertContext(mapped, average), nowUtc);
            }

            // 提醒状态点：由纯逻辑解析器把各判定源状态合并为单一枚举（取"是否需要用户动手"最高者）
            UpdateIndicator(nowUtc, average, shortAverage, scheduleGateSuppressed);

            if (!decision.ShouldAlert) return;

            // 提醒已执行（Decide 内部经 NotifyRequested 发出）→ 进入防自激，
            // 并清空样本窗口，避免残留高值在恢复后立即再次触发。
            _pauseUntilUtc = DateTime.UtcNow.Add(AntiSelfKickDelay);
            _window.Clear();
        }
        catch
        {
            // 采样/判定任一步骤失败都不应抛出到定时器循环
        }
    }

    /// <summary>
    /// 解析提醒状态并更新组件读取的显示属性。
    /// </summary>
    /// <param name="nowUtc">当前时间（UTC）。</param>
    /// <param name="average">判定用的滑动窗口平均分贝。</param>
    /// <param name="shortAverage">冷却期数字判据所用的 1 秒短窗口平均分贝。</param>
    /// <param name="scheduleGateSuppressed">本拍是否被时段闸门抑制（抑制时判定源未被推进，其状态不可用于显示）。</param>
    private void UpdateIndicator(DateTime nowUtc, double average, double shortAverage, bool scheduleGateSuppressed)
    {
        var threshold = GetIndicatorThreshold();

        bool windowOpen;
        bool triggerActive;
        bool coolingDown;
        TimeSpan remaining;

        if (scheduleGateSuppressed)
        {
            // 闸门生效期间判定源本拍未被推进，其内部状态（超阈值 / 冷却 / 筛选窗口）停留在闸门开始前的旧值，
            // 因此不参与显示。状态点改由"当前窗口平均是否超阈值"直接驱动，
            // 只回答"现在吵不吵"——与"课间只停通知、照常显示噪音"的约定一致。
            windowOpen = false;
            triggerActive = IsAnySourceEnabled && average > threshold;
            coolingDown = false;
            remaining = TimeSpan.Zero;
        }
        else
        {
            windowOpen = _hotkeySource.IsWindowOpen;
            var autoCoolingDown = _autoSource.IsCoolingDown(nowUtc);
            var hotkeyCoolingDown = _hotkeySource.IsCoolingDown(nowUtc);

            remaining = windowOpen && _hotkeySource.WindowOpenUntilUtc > nowUtc
                ? _hotkeySource.WindowOpenUntilUtc - nowUtc
                : TimeSpan.Zero;

            // "正在超阈值"只在判定源未处于冷却期时成立：提醒一旦发出即进入冷却，
            // 此后由冷却态表达形状（空心方，表示"提醒已发生过"），
            // "现在是否还在吵"交给数字颜色（冷却期改用 1 秒短窗口平均）。
            triggerActive =
                (_autoSource.IsTriggerActive && !autoCoolingDown) ||
                (_hotkeySource.IsTriggerActive && !hotkeyCoolingDown);
            coolingDown = autoCoolingDown || hotkeyCoolingDown;
        }

        var snapshot = _indicatorResolver.Update(new IndicatorInputs
        {
            HasSample = HasSample,
            AnySourceEnabled = IsAnySourceEnabled,
            AnyWindowOpen = windowOpen,
            AnyTriggerActive = triggerActive,
            AnyCoolingDown = coolingDown,
            AverageDecibel = average,
            ShortWindowAverageDecibel = shortAverage,
            Threshold = threshold,
            HotkeyWindowRemaining = remaining,
            HotkeyWindowTimeout = _hotkeySource.WindowTimeout,
        }, nowUtc);

        IndicatorState = snapshot.State;
        IndicatorValueState = snapshot.ValueState;
        IsIndicatorBreathing = snapshot.IsBreathing;
        IndicatorStateChanged = snapshot.Changed;
    }

    /// <summary>
    /// 推进时段闸门，返回本拍是否应抑制提醒。
    /// </summary>
    /// <remarks>
    /// 宿主课程服务不可用时按"不抑制"处理，行为回到未接入闸门之前；课表未启用或未加载时由闸门内部旁路。
    /// 服务惰性解析并缓存（宿主服务在本插件加载时通常已就绪，解析失败则下一拍重试）。
    /// </remarks>
    private bool EvaluateScheduleGate(DateTime nowUtc)
    {
        var lessons = _lessonsService ??= IAppHost.TryGetService<ILessonsService>();
        if (lessons is null) return false;

        var settings = _settingsService?.Settings;
        return _scheduleGate.Update(
            ToSchedulePhase(lessons.CurrentState),
            lessons.IsClassPlanEnabled,
            lessons.IsClassPlanLoaded,
            settings?.SuppressAlertDuringBreak ?? true,
            settings?.ClassStartProtectionMinutes ?? 3,
            nowUtc);
    }

    /// <summary>把宿主的 <see cref="TimeState"/> 映射为闸门关心的时段类别（准备上课、放学等一律不抑制）。</summary>
    private static SchedulePhase ToSchedulePhase(TimeState state) => state switch
    {
        TimeState.OnClass => SchedulePhase.OnClass,
        TimeState.Breaking => SchedulePhase.Breaking,
        _ => SchedulePhase.Other,
    };

    /// <summary>
    /// 数字颜色判据的阈值：取已启用判定源中的最小阈值。
    /// 各源的判据共用同一数值，因此"任一源超阈"等价于"数值超过最小阈值"；未启用任何源时取 0（该情形下判据为未知）。
    /// </summary>
    private double GetIndicatorThreshold()
    {
        var threshold = double.MaxValue;
        if (_autoSource.IsEnabled) threshold = Math.Min(threshold, _autoSource.Threshold);
        if (_hotkeySource.IsEnabled) threshold = Math.Min(threshold, _hotkeySource.Threshold);
        return threshold == double.MaxValue ? 0.0 : threshold;
    }

    /// <summary>
    /// 剔除 1 秒之前的样本并返回短窗口平均；窗口内无样本时返回 <paramref name="fallback"/>（最近一次采样值）。
    /// </summary>
    private double GetShortWindowAverage(DateTime nowUtc, double fallback)
    {
        while (_shortWindow.Count > 0 && nowUtc - _shortWindow.Peek().TimestampUtc > ShortWindowDuration)
        {
            _shortWindow.Dequeue();
        }

        if (_shortWindow.Count == 0) return fallback;

        double sum = 0.0;
        foreach (var sample in _shortWindow) sum += sample.Value;
        return sum / _shortWindow.Count;
    }

    /// <summary>把插件全局设置同步到各判定源与仲裁模块（设置页改动下一拍即生效）。</summary>
    private void ApplySettings()
    {
        var settings = _settingsService?.Settings;
        if (settings is null) return;

        _autoSource.IsEnabled = settings.AutoSourceEnabled;
        _autoSource.Threshold = settings.AutoSourceThreshold;
        _autoSource.Cooldown = TimeSpan.FromSeconds(Math.Max(0, settings.AutoSourceCooldownSeconds));

        _hotkeySource.IsEnabled = settings.HotkeySourceEnabled;
        _hotkeySource.Threshold = settings.HotkeySourceThreshold;
        _hotkeySource.Cooldown = TimeSpan.FromSeconds(Math.Max(0, settings.HotkeySourceCooldownSeconds));
        _hotkeySource.WindowTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.HotkeyWindowSeconds, 1, 600));
        _hotkeySource.Hotkey = new HotkeyDefinition(settings.HotkeyKey ?? string.Empty, ComposeModifiers(settings));

        // 优先级顺序变化时才重排（避免每拍重复排序）
        var order = settings.SourcePriorityOrder;
        if (_appliedPriorityOrder is null || !order.SequenceEqual(_appliedPriorityOrder))
        {
            _coordinator.ApplyPriorityOrder(order);
            _appliedPriorityOrder = order.ToArray();
        }
    }

    /// <summary>把设置中的四个修饰键开关组合为热键修饰键位标志。</summary>
    private static HotkeyModifiers ComposeModifiers(DecibelMonitorGlobalSettings settings)
    {
        var modifiers = HotkeyModifiers.None;
        if (settings.HotkeyCtrl) modifiers |= HotkeyModifiers.Ctrl;
        if (settings.HotkeyAlt) modifiers |= HotkeyModifiers.Alt;
        if (settings.HotkeyShift) modifiers |= HotkeyModifiers.Shift;
        if (settings.HotkeyMeta) modifiers |= HotkeyModifiers.Meta;
        return modifiers;
    }

    /// <summary>按平均窗口时长与采样节拍换算窗口样本容量（至少 1，最多 <see cref="MaxWindowSamples"/>）。</summary>
    private int GetWindowCapacity()
    {
        var seconds = _settingsService?.Settings.AverageWindowSeconds ?? 3.0;
        var count = (int)Math.Round(seconds * 1000.0 / SampleInterval.TotalMilliseconds);
        return Math.Clamp(count, 1, MaxWindowSamples);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.NotifyRequested -= OnNotifyRequested;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }
}