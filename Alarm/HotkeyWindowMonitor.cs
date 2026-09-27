using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Shared;
using Decibel_Monitor.Alerting;
using KeyboardCapture.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Decibel_Monitor.Alarm;

/// <summary>
/// 热键判定源的宿主接入：解析 KeyboardCapture 插件的全局键盘服务，把筛选窗口期间的
/// <see cref="IKeyboardCaptureService.KeyDown"/> 事件转发给
/// <see cref="HotkeyConfirmDecisionSource.HandleKeyPress"/>。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>KeyboardCapture 为<strong>非必需依赖</strong>：服务未解析到时降级为空转，
/// 本插件其余功能不受影响；解析前定时重试（KeyboardCapture 插件加载顺序不保证）。</description></item>
/// <item><description>键盘事件在<strong>后台线程</strong>触发，而判定源状态由 UI 线程的采样编排推进，
/// 为避免竞态，按键处理统一封送到 <see cref="Dispatcher.UIThread"/> 后执行。</description></item>
/// <item><description>筛选窗口的开/关状态由 <see cref="HotkeyConfirmDecisionSource"/> 自身维护，
/// 组件经 <see cref="AlertRuntimeService"/> 读取，本服务不做状态轮询。</description></item>
/// </list>
/// </remarks>
public sealed class HotkeyWindowMonitor : IHostedService, IDisposable
{
    /// <summary>解析 KeyboardCapture 服务失败后的重试间隔。</summary>
    private static readonly TimeSpan ResolveRetryInterval = TimeSpan.FromSeconds(2);

    private readonly HotkeyConfirmDecisionSource _source;
    private readonly object _gate = new();
    private Timer? _retryTimer;
    private IKeyboardCaptureService? _capture;
    private bool _disposed;

    /// <param name="source">热键判定源（由 DI 以单例注入）。</param>
    public HotkeyWindowMonitor(HotkeyConfirmDecisionSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        TryResolveAndSubscribe();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    /// <summary>尝试解析 KeyboardCapture 服务；未成功则安排定时重试，成功后订阅按键事件。</summary>
    private void TryResolveAndSubscribe()
    {
        if (_disposed || _capture is not null) return;

        var service = IAppHost.TryGetService<IKeyboardCaptureService>();
        if (service is null)
        {
            // KeyboardCapture 插件可能晚于本插件加载，定时重试直到解析成功
            lock (_gate)
            {
                if (_disposed) return;
                _retryTimer?.Dispose();
                _retryTimer = new Timer(
                    _ => Dispatcher.UIThread.Post(TryResolveAndSubscribe),
                    null,
                    (int)ResolveRetryInterval.TotalMilliseconds,
                    (int)ResolveRetryInterval.TotalMilliseconds);
            }
            return;
        }

        lock (_gate)
        {
            if (_disposed) return;
            _retryTimer?.Dispose();
            _retryTimer = null;
            _capture = service;
            service.KeyDown += OnKeyDown;
        }
    }

    /// <summary>
    /// 全局按键按下事件（后台线程触发）。仅在筛选窗口开启期间转发给判定源，命中热键即确认。
    /// </summary>
    private void OnKeyDown(object? sender, KeyboardKeyEventArgs e)
    {
        // 未开窗或未启用时不消费按键，直接返回避免无谓封送
        if (!_source.IsEnabled || !_source.IsWindowOpen) return;

        // 封送到 UI 线程，与采样编排（判定源状态推进）同线程，避免竞态
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            try
            {
                _source.HandleKeyPress(e.Key.Name, ToHotkeyModifiers(e.Modifiers), DateTime.UtcNow);
            }
            catch
            {
                // 按键处理失败不影响全局钩子分发
            }
        });
    }

    /// <summary>把 KeyboardCapture 的修饰键位标志映射为本插件判定源使用的修饰键位标志。</summary>
    private static HotkeyModifiers ToHotkeyModifiers(KeyboardCapture.Abstractions.KeyModifiers modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyboardCapture.Abstractions.KeyModifiers.Ctrl)) result |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(KeyboardCapture.Abstractions.KeyModifiers.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyboardCapture.Abstractions.KeyModifiers.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyboardCapture.Abstractions.KeyModifiers.Meta)) result |= HotkeyModifiers.Meta;
        return result;
    }

    private void Unsubscribe()
    {
        lock (_gate)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
            if (_capture is not null)
            {
                _capture.KeyDown -= OnKeyDown;
                _capture = null;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unsubscribe();
    }
}