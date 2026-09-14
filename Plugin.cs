using System;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
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
            // 可选欢迎提示
            //CommonTaskDialogs.ShowDialog("Hello world!", "Hello from Decibel_Monitor!");

            // 插件全局设置（保存于插件配置目录，供采样服务与设置页共享）
            var globalSettingsService = new Services.DecibelMonitorSettingsService(PluginConfigFolder);
            services.AddSingleton(globalSettingsService);

            // 共享的麦克风峰值采样服务（单例，构造时读取全局采样偏好）
            services.AddSingleton<Services.AudioPeakMeter>();

            // 一次性注册组件与其设置控件（不要重复注册）
            services.AddComponent<Controls.Components.DecibelComponent, Controls.ComponentSettings.DecibelComponentSettingsControl>();
        }
    }
}
