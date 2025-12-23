using System;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NAudio.CoreAudioApi;

namespace Decibel_Monitor
{
    [PluginEntrance]
    public class Plugin : PluginBase
    {
        public override void Initialize(HostBuilderContext context, IServiceCollection services)
        {
            // 可选欢迎提示
            CommonTaskDialogs.ShowDialog("Hello world!", "Hello from Decibel_Monitor!");

            // 一次性注册组件与其设置控件（不要重复注册）
            services.AddComponent<Controls.Components.DecibelComponent, Controls.ComponentSettings.DecibelComponentSettingsControl>();
        }

        public float GetVoicePeakValue()
        {
            var enumerator = new MMDeviceEnumerator();
            var CaptureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var selectedDevice = CaptureDevices.FirstOrDefault(c => c.ID == defaultDevice.ID);
            return selectedDevice?.AudioMeterInformation.MasterPeakValue ?? 0f;
        }
    }
}