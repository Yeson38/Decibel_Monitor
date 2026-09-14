using System;
using ClassIsland.Core.Abstractions.Controls;
using Decibel_Monitor.Models.ComponentSettings;

namespace Decibel_Monitor.Controls.ComponentSettings;

/// <summary>
/// 分贝组件设置控件（"小组件显示侧"）。
/// 仅包含该组件的显示偏好；测量/校准（全局）与提醒设置分别位于插件设置页与通知提供方设置。
/// </summary>
public partial class DecibelComponentSettingsControl : ComponentBase<DecibelComponentSettings>, IDisposable
{
    private bool _disposed;

    public DecibelComponentSettingsControl()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}


