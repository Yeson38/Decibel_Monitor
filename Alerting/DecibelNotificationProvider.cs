using System;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Decibel_Monitor.Models.NotificationProviderSettings;
using Microsoft.Extensions.Logging;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 分贝提醒通知提供方。
/// 负责发出 ClassIsland 醒目提醒（强调通知与语音均由宿主通知系统负责），
/// 文案来自设置中的 <see cref="DecibelNotificationProviderSettings.AlertText"/>。
/// </summary>
/// <remarks>
/// 边界说明：<strong>本提供方只负责"发什么内容"，不做阈值/冷却/启停判定</strong>——
/// 那部分收敛到插件设置页的判定源（<see cref="IAlertDecisionSource"/>）与
/// 仲裁模块（<see cref="AlertDecisionCoordinator"/>），避免双重判定门槛。
/// 宿主接入仍走 <see cref="NotificationProviderBase{TSettings}"/> 以获取自动持久化的设置，
/// 并经 <see cref="NotificationChannelInfo"/> 渠道发送，与 ClassIsland 内置提供方一致。
/// </remarks>
[NotificationProviderInfo(
    "54f1b836-efa4-4755-bbac-7460f697cbb0",
    "分贝提醒",
    "\uE02A",
    "当麦克风分贝值超出设定阈值时发出醒目提醒。"
)]
[NotificationChannelInfo(
    OverThresholdChannelId,
    "超过分贝阈值",
    "\uE02A",
    description: "当麦克风分贝值超过设定阈值时发出的提醒。"
)]
public sealed class DecibelNotificationProvider : NotificationProviderBase<DecibelNotificationProviderSettings>, IDisposable
{
    /// <summary>"超过分贝阈值"提醒渠道 GUID。</summary>
    public const string OverThresholdChannelId = "39d7bd60-b8a1-4b2f-a099-71c576761064";

    private static DecibelNotificationProvider? _instance;

    private readonly ILogger<DecibelNotificationProvider>? _logger;

    /// <summary>
    /// 当前通知提供方实例（宿主启动后由 DI 以单例方式创建）。
    /// </summary>
    public static DecibelNotificationProvider? Instance => _instance;

    public DecibelNotificationProvider(ILogger<DecibelNotificationProvider>? logger = null)
    {
        _logger = logger;
        _instance = this;
    }

    /// <summary>
    /// 发出一次"超过分贝阈值"提醒，文案取设置中的提醒正文。
    /// 调用方（仲裁模块 / 运行时编排）负责判定"是否该提醒"。
    /// </summary>
    /// <param name="text">提醒正文；为空时回退到设置中的提醒正文。</param>
    public void Notify(string? text = null)
    {
        try
        {
            NotifyOverThreshold(string.IsNullOrWhiteSpace(text) ? Settings.AlertText : text);
        }
        catch (Exception ex)
        {
            // 通知失败不应影响主功能
            _logger?.LogWarning(ex, "发送分贝提醒失败。");
        }
    }

    /// <summary>
    /// 发送"超过分贝阈值"提醒。
    /// </summary>
    /// <param name="text">提醒正文。</param>
    private void NotifyOverThreshold(string text)
    {
        var request = new NotificationRequest
        {
            ChannelId = Guid.Parse(OverThresholdChannelId),
            MaskContent = NotificationContent.CreateSimpleTextContent(text),
            OverlayContent = NotificationContent.CreateSimpleTextContent(text, x => x.Duration = TimeSpan.FromSeconds(30)),
        };
        Channel(OverThresholdChannelId).ShowNotification(request);
    }

    /// <summary>
    /// 释放时清理静态实例引用，避免插件重载或宿主重启后残留已失效的引用。
    /// </summary>
    public void Dispose()
    {
        if (ReferenceEquals(_instance, this))
        {
            _instance = null;
        }
    }
}