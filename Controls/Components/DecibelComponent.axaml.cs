using System;
using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Decibel_Monitor.Services;
using DecibelComponentSettings = Decibel_Monitor.Models.ComponentSettings.DecibelComponentSettings;

namespace Decibel_Monitor.Controls.Components;

[ComponentInfo(
    "3540a8df-963c-45e9-a42d-8a55824994d5",
    "分贝值",
    "\uEB88",
    "在主界面上显示麦克风分贝值。"
)]
public partial class DecibelComponent : ComponentBase<DecibelComponentSettings>, IDisposable
{
    /// <summary>当前显示的分贝值文本。</summary>
    public static readonly StyledProperty<string> CurrentDecibelValueProperty =
        AvaloniaProperty.Register<DecibelComponent, string>(nameof(CurrentDecibelValue), "N/A");

    /// <summary>当前是否处于"超过阈值"提醒状态（显示提示文字）。</summary>
    public static readonly StyledProperty<bool> IsAlertActiveProperty =
        AvaloniaProperty.Register<DecibelComponent, bool>(nameof(IsAlertActive));

    /// <summary>超过阈值时显示的提示文字。</summary>
    public static readonly StyledProperty<string> AlertDisplayTextProperty =
        AvaloniaProperty.Register<DecibelComponent, string>(nameof(AlertDisplayText), "请保持安静");

    private readonly DispatcherTimer _updateTimer;
    private readonly AudioPeakMeter? _audioPeakMeter;
    private readonly DecibelMonitorSettingsService? _settingsService;
    private volatile bool _disposed;
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
    /// 当前是否处于"超过阈值"提醒状态（用于显示提示文字）。
    /// </summary>
    public bool IsAlertActive
    {
        get => GetValue(IsAlertActiveProperty);
        private set => SetValue(IsAlertActiveProperty, value);
    }

    /// <summary>
    /// 超过阈值时显示的提示文字（取通知设置中的自定义文字）。
    /// </summary>
    public string AlertDisplayText
    {
        get => GetValue(AlertDisplayTextProperty);
        private set => SetValue(AlertDisplayTextProperty, value);
    }

    public DecibelComponent()
    {
        InitializeComponent();

        // 从宿主 DI 容器获取共享的峰值采样服务（单例，含并发保护与缓存）与全局设置
        _audioPeakMeter = IAppHost.Host?.Services.GetService(typeof(AudioPeakMeter)) as AudioPeakMeter;
        _settingsService = IAppHost.Host?.Services.GetService(typeof(DecibelMonitorSettingsService)) as DecibelMonitorSettingsService;

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    // 异步 Tick，内部会异步采样（不会阻塞 UI）
    private async void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        // 节流：上一次采样尚未结束时跳过本次 Tick，避免耗时采样导致异步任务堆积
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

            if (_audioPeakMeter is null)
            {
                CurrentDecibelValue = "无采样服务";
                return;
            }

            // 优先走实时计量；不可用时自动使用短时录音回退采样（带缓存与并发保护）
            float linear = await _audioPeakMeter.GetDefaultDevicePeakLinearAsync().ConfigureAwait(false);

            // 放大倍数为全局校准结果（同一麦克风所有组件显示一致）
            double magnification = _settingsService?.Settings.Magnification ?? 1.0;
            double mapped = DecibelCalculator.LinearToDisplayDb(linear, magnification);

            // 绑定属性变更与提醒评估（含通知触发）都放回 UI 线程执行。
            // 提醒提供方实例采用"动态读取"而非构造时缓存：插件加载早期组件可能先于
            // 通知提供方（IHostedService）创建，缓存为 null 将导致提醒永久失效。
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;

                var state = DecibelNotificationProvider.Instance?.Evaluate(mapped)
                            ?? new DecibelAlertState(false, "请保持安静");

                // "组件内提示"开关只影响组件上是否显示提示文字，不影响 Evaluate 触发的系统通知
                bool showAlertText = Settings?.ShowAlertTextOnComponent ?? true;
                bool showPrefix = Settings?.ShowDecibelPrefix ?? true;

                CurrentDecibelValue = showPrefix ? $"分贝: {mapped:F1}" : $"{mapped:F1}";
                IsAlertActive = showAlertText && state.IsActive;
                AlertDisplayText = state.Text;
            });
        }
        catch (Exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                CurrentDecibelValue = "读取失败";
            });
        }
        finally
        {
            _isUpdating = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateTimer.Tick -= UpdateTimer_Tick;
        _updateTimer.Stop();
    }
}

