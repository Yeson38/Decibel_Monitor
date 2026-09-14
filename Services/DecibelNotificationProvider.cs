using System;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using Decibel_Monitor.Models.NotificationProviderSettings;
using Microsoft.Extensions.Logging;

namespace Decibel_Monitor.Services;

/// <summary>
/// 分贝提醒通知提供方。
/// 负责集中评估"当前分贝值是否超过阈值"并触发 ClassIsland 提醒（带冷却防刷屏），
/// 同时向组件返回评估状态用于显示提示文字。
/// </summary>
/// <remarks>
/// 修复说明：
/// 1. 由非泛型 <see cref="NotificationProviderBase"/> 改为泛型
///    <see cref="NotificationProviderBase{TSettings}"/>，从而获得宿主自动持久化的设置
///    （"强调通知侧"的设置项），并声明 <see cref="NotificationProviderInfo.HasSettings"/>。
/// 2. 注册 <see cref="NotificationChannelInfo"/> 渠道，并通过 <c>Channel(...)</c> 发送，
///    与 ClassIsland 内置通知提供方保持一致，避免使用空渠道 GUID。
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

    private DateTime _nextAlertTimeUtc = DateTime.MinValue;

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
    /// 评估当前分贝映射值是否超阈值；需要时（超阈值且冷却已过）发送提醒。
    /// 供组件在每个刷新周期调用，避免组件自己维护提醒状态。
    /// </summary>
    /// <param name="mappedDb">当前分贝映射值（显示刻度 0..150）。</param>
    /// <returns>评估状态：是否处于"超过阈值"提醒状态及提醒文字。</returns>
    public DecibelAlertState Evaluate(double mappedDb)
    {
        try
        {
            var text = Settings.AlertText;

            // 判定逻辑位于 DecibelAlertEvaluator（纯函数，便于单元测试）
            var decision = DecibelAlertEvaluator.Evaluate(
                mappedDb,
                Settings.AlertThreshold,
                Settings.IsAlertEnabled,
                DateTime.UtcNow,
                _nextAlertTimeUtc,
                Settings.AlertCooldownMinutes);

            if (decision.ShouldNotify)
            {
                _nextAlertTimeUtc = decision.NextAlertTimeUtc;
                NotifyOverThreshold(text);
            }

            return new DecibelAlertState(decision.IsActive, text);
        }
        catch (Exception ex)
        {
            // 评估失败不应影响组件主功能，仅记录日志以便诊断
            _logger?.LogWarning(ex, "评估分贝提醒状态失败。");
            return new DecibelAlertState(false, Settings.AlertText);
        }
    }

    /// <summary>
    /// 发送"超过分贝阈值"提醒。
    /// </summary>
    /// <param name="text">提醒正文。</param>
    private void NotifyOverThreshold(string text)
    {
        try
        {
            var request = new NotificationRequest
            {
                ChannelId = Guid.Parse(OverThresholdChannelId),
                MaskContent = NotificationContent.CreateSimpleTextContent(text),
                OverlayContent = NotificationContent.CreateSimpleTextContent(text, x => x.Duration = TimeSpan.FromSeconds(30)),
            };
            Channel(OverThresholdChannelId).ShowNotification(request);
        }
        catch (Exception ex)
        {
            // 通知失败不应影响组件的主功能
            _logger?.LogWarning(ex, "发送分贝提醒失败。");
        }
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

