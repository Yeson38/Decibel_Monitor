using System;
using System.IO;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Decibel_Monitor
{
    [PluginEntrance]
    public class Plugin : PluginBase
    {
        public override void Initialize(HostBuilderContext context, IServiceCollection services)
        {
            // 插件全局设置（保存于插件配置目录，供设置页与采样服务共享）
            var globalSettingsService = new Services.DecibelMonitorSettingsService(PluginConfigFolder);
            services.AddSingleton(globalSettingsService);

            // 共享的麦克风峰值采样服务（单例，构造时读取全局采样偏好）
            services.AddSingleton<Measurement.AudioPeakMeter>();

            // ── 提醒链路：判定源 → 仲裁模块 → 运行时编排 ──
            // 判定源各自独立启用（启用状态与参数由设置页控制），默认均未启用。
            services.AddSingleton<Alerting.AutoThresholdDecisionSource>();
            services.AddSingleton<Alerting.HotkeyConfirmDecisionSource>();
            services.AddSingleton<Alerting.IAlertDecisionSource>(
                sp => sp.GetRequiredService<Alerting.AutoThresholdDecisionSource>());
            services.AddSingleton<Alerting.IAlertDecisionSource>(
                sp => sp.GetRequiredService<Alerting.HotkeyConfirmDecisionSource>());

            // 仲裁模块：自持判定源集合与优先级配置，并归口发出提醒通知
            services.AddSingleton(sp => new Alerting.AlertDecisionCoordinator(
                sp.GetServices<Alerting.IAlertDecisionSource>(),
                sourcePriorityOrder: null));

            // 热键筛选窗口监视：KeyboardCapture 为非必需依赖，未安装时降级为空转
            services.AddSingleton<Alarm.HotkeyWindowMonitor>();
            services.AddHostedService(sp => sp.GetRequiredService<Alarm.HotkeyWindowMonitor>());

            // 提醒运行时编排：采样 → 判定 → 执行提醒 → 防自激
            services.AddSingleton<Alarm.AlertRuntimeService>();
            services.AddHostedService(sp => sp.GetRequiredService<Alarm.AlertRuntimeService>());

            // 一次性注册组件与其设置控件（不要重复注册）
            services.AddComponent<Controls.Components.DecibelComponent, Controls.ComponentSettings.DecibelComponentSettingsControl>();

            // 分贝提醒通知提供方（含"强调通知侧"设置控件）
            services.AddNotificationProvider<Alerting.DecibelNotificationProvider,
                Controls.NotificationProviders.DecibelNotificationProviderSettingsControl>();

            // 插件设置页（与其他设置项同层级）
            services.AddSettingsPage<Views.SettingsPages.DecibelMonitorSettingsPage>();

            // CI 集成测试标记：仅当显式开启环境变量时写入，
            // 供 GitHub Actions 在启动 ClassIsland 后确认插件已成功加载（Initialize 完整执行、注册无异常）。
            if (Environment.GetEnvironmentVariable("DECIBEL_MONITOR_CI_MARKER") == "1")
            {
                try
                {
                    Directory.CreateDirectory(PluginConfigFolder);
                    File.WriteAllText(
                        Path.Combine(PluginConfigFolder, "loaded.marker"),
                        $"loaded at {DateTime.UtcNow:O}");
                }
                catch
                {
                    // 标记写入失败不应影响插件正常初始化
                }
            }
        }
    }
}
