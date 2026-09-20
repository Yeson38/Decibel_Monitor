using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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

    private readonly AudioPeakMeter _audioPeakMeter;
    private readonly DecibelMonitorSettingsService? _settingsService;
    private readonly AlertDecisionCoordinator _coordinator;
    private readonly AutoThresholdDecisionSource _autoSource;
    private readonly HotkeyConfirmDecisionSource _hotkeySource;
    private readonly List<float> _window = new();
    private readonly DispatcherTimer _timer;
    private DateTime _pauseUntilUtc = DateTime.MinValue;
    private string[]? _appliedPriorityOrder;
    private bool _disposed;

    /// <summary>最新一次采样的分贝映射值（显示刻度 0..150）。</summary>
    public double CurrentDecibel { get; private set; }

    /// <summary>是否已取得过至少一次采样（未取得时组件显示占位文本）。</summary>
    public bool HasSample { get; private set; }

    /// <summary>
    /// 当前是否应点亮提醒状态点（红）：任一判定源处于超阈值、筛选窗口开启或冷却期。
    /// 绿点表示上述条件均不成立。
    /// </summary>
    public bool IsAlertStateActive { get; private set; }

    /// <summary>是否至少启用了一个判定源（决定组件是否显示提醒状态点）。</summary>
    public bool IsAnySourceEnabled => _autoSource.IsEnabled || _hotkeySource.IsEnabled;

    /// <summary>热键判定源所处的筛选窗口是否开启（组件据此把状态点显示为红绿闪动）。</summary>
    public bool IsHotkeyWindowOpen => _hotkeySource.IsWindowOpen;

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

            var decision = _coordinator.Decide(new AlertContext(mapped, average), DateTime.UtcNow);

            // 提醒状态点：超阈值、筛选窗口开启、以及提醒后的冷却期都点亮（红点），其余为绿点
            var nowUtc = DateTime.UtcNow;
            IsAlertStateActive = _autoSource.IsTriggerActive
                || _hotkeySource.IsTriggerActive
                || _hotkeySource.IsWindowOpen
                || _autoSource.IsCoolingDown(nowUtc)
                || _hotkeySource.IsCoolingDown(nowUtc);

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