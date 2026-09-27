using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Shared;
using Decibel_Monitor.Alerting;
using Decibel_Monitor.Measurement;
using Decibel_Monitor.Models;
using Decibel_Monitor.Services;
using KeyboardCapture.Abstractions;

namespace Decibel_Monitor.Views.SettingsPages;

/// <summary>判定源优先级列表中的一项（视图模型，仅供设置页列表展示与排序）。</summary>
/// <param name="Id">判定源标识（写回设置用）。</param>
/// <param name="DisplayName">判定源显示名。</param>
/// <param name="Description">判定源简要说明。</param>
public sealed record AlertSourcePriorityItem(string Id, string DisplayName, string Description);

/// <summary>
/// Decibel_Monitor 插件设置页（与其他设置项同层级）。
/// 承载插件全局配置、提醒判定源设置与设备诊断。
/// </summary>
[SettingsPageInfo(
    "Decibel_Monitor.PluginSettings",
    "Decibel_Monitor",
    "\uEB88",
    "\uEB88",
    category: SettingsPageCategory.External)]
public partial class DecibelMonitorSettingsPage : SettingsPageBase
{
    private readonly AudioPeakMeter? _audioPeakMeter;
    private readonly AlertDecisionCoordinator? _coordinator;

    /// <summary>
    /// 插件全局设置（用于绑定采样偏好与各判定源设置）。
    /// </summary>
    public DecibelMonitorGlobalSettings GlobalSettings { get; }

    /// <summary>
    /// 判定源优先级列表（按设置中的顺序排列，可经“上移/下移”调整）。
    /// </summary>
    public ObservableCollection<AlertSourcePriorityItem> PriorityItems { get; } = new();

    /// <summary>
    /// KeyboardCapture 插件是否可用（不可用时禁用“热键确认”判定源的全部设置项）。
    /// </summary>
    public bool KeyboardCaptureAvailable { get; }

    /// <summary>
    /// 供 XAML 资源加载器 / 设计器使用的默认构造。
    /// </summary>
    public DecibelMonitorSettingsPage() : this(new DecibelMonitorSettingsService(null), null, null)
    {
    }

    public DecibelMonitorSettingsPage(
        DecibelMonitorSettingsService settingsService,
        AudioPeakMeter? audioPeakMeter = null,
        AlertDecisionCoordinator? coordinator = null)
    {
        InitializeComponent();
        GlobalSettings = settingsService.Settings;
        _audioPeakMeter = audioPeakMeter;
        _coordinator = coordinator;
        KeyboardCaptureAvailable = IAppHost.TryGetService<IKeyboardCaptureService>() is not null;

        DataContext = this;
        RebuildPriorityItems();
        RefreshDefaultDeviceText();
        RefreshMagnificationText();
    }

    /// <summary>
    /// 按设置中的优先级顺序重建列表；设置中未列出的判定源按仲裁模块的顺序追加到末尾。
    /// </summary>
    private void RebuildPriorityItems()
    {
        PriorityItems.Clear();
        if (_coordinator is null) return;

        var sources = _coordinator.Sources;
        var ordered = new List<IAlertDecisionSource>();
        foreach (var id in GlobalSettings.SourcePriorityOrder ?? new List<string>())
        {
            var matched = sources.FirstOrDefault(s => s.Id == id);
            if (matched is not null && ordered.All(o => o.Id != matched.Id)) ordered.Add(matched);
        }

        foreach (var source in sources)
        {
            if (ordered.All(o => o.Id != source.Id)) ordered.Add(source);
        }

        foreach (var source in ordered)
        {
            PriorityItems.Add(new AlertSourcePriorityItem(source.Id, source.DisplayName, source.Description));
        }
    }

    /// <summary>把列表当前顺序写回设置（运行时编排服务会在下一拍应用到仲裁模块）。</summary>
    private void SavePriorityOrder()
    {
        GlobalSettings.SourcePriorityOrder = PriorityItems.Select(i => i.Id).ToList();
    }

    /// <summary>"上移"按钮：把选中判定源的优先级提高一位。</summary>
    public void On_MovePriorityUp(object? sender, RoutedEventArgs e) => MovePriority(-1);

    /// <summary>"下移"按钮：把选中判定源的优先级降低一位。</summary>
    public void On_MovePriorityDown(object? sender, RoutedEventArgs e) => MovePriority(1);

    private void MovePriority(int offset)
    {
        var index = ListPriorityOrder.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= PriorityItems.Count) return;

        PriorityItems.Move(index, target);
        ListPriorityOrder.SelectedIndex = target;
        SavePriorityOrder();
    }

    /// <summary>
    /// 刷新默认捕获设备描述文本。
    /// </summary>
    private void RefreshDefaultDeviceText()
    {
        try
        {
            var desc = _audioPeakMeter?.GetDefaultCaptureDeviceDescription();
            TextDefaultDevice.Text = string.IsNullOrWhiteSpace(desc)
                ? "（未发现可用的默认捕获设备）"
                : $"当前默认捕获设备：{desc}";
        }
        catch
        {
            TextDefaultDevice.Text = "（读取默认捕获设备失败）";
        }
    }

    /// <summary>
    /// 刷新当前全局放大倍数文本。
    /// </summary>
    private void RefreshMagnificationText()
    {
        TextMagnification.Text = $"{GlobalSettings.Magnification:F3}×";
    }

    /// <summary>
    /// 校准按钮：短暂采样一次默认麦克风，按全局参考 dB 计算并保存全局放大倍数。
    /// </summary>
    public async void On_Calibrate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_audioPeakMeter is null)
            {
                _ = CommonTaskDialogs.ShowDialog("校准失败", "采样服务不可用。");
                return;
            }

            float measuredLinear = await _audioPeakMeter
                .CaptureSamplePeakAsync(_audioPeakMeter.DefaultCaptureMs).ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (measuredLinear <= 0f)
                {
                    try
                    {
                        _ = CommonTaskDialogs.ShowDialog("校准结果",
                            "未检测到有效输入，无法完成校准。请确认麦克风已启用并有声源。");
                    }
                    catch { }
                    return;
                }

                double targetDb = GlobalSettings.ReferenceDecibel;
                double magnification = DecibelCalculator.CalculateMagnification(targetDb, measuredLinear);
                double adjustedLinear = measuredLinear * magnification;
                double adjustedDb = adjustedLinear > 0 ? 20.0 * Math.Log10(adjustedLinear) : double.NegativeInfinity;
                double measuredDb = measuredLinear > 0 ? 20.0 * Math.Log10(measuredLinear) : double.NegativeInfinity;

                GlobalSettings.Magnification = magnification;
                RefreshMagnificationText();

                try
                {
                    _ = CommonTaskDialogs.ShowDialog("校准结果",
                        $"测量线性值: {measuredLinear:F4}\n" +
                        $"测量 dBFS: {measuredDb:F1} dB\n" +
                        $"目标 dB (显示): {targetDb:F1} dB\n" +
                        $"目标 dBFS: {DecibelCalculator.DisplayDbToDbFs(targetDb):F1} dBFS\n" +
                        $"全局放大倍数: {magnification:F6}×\n" +
                        $"放大后 dBFS（验证）: {adjustedDb:F1} dBFS\n\n" +
                        "该校准对插件内所有组件生效。");
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            try { _ = CommonTaskDialogs.ShowDialog("校准失败", $"发生异常：{ex.Message}"); } catch { }
        }
    }

    /// <summary>
    /// "刷新设备"按钮点击事件。
    /// </summary>
    public void On_RefreshDevices(object? sender, RoutedEventArgs e)
    {
        RefreshDefaultDeviceText();
    }

    /// <summary>
    /// "捕获设备列表"按钮点击事件。
    /// </summary>
    public void On_ShowDevices(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_audioPeakMeter is null)
            {
                _ = CommonTaskDialogs.ShowDialog("捕获设备列表", "采样服务不可用。");
                return;
            }

            var devices = _audioPeakMeter.GetCaptureDeviceDescriptions();
            var text = string.Join("\n", devices);
            _ = CommonTaskDialogs.ShowDialog("捕获设备列表",
                string.IsNullOrWhiteSpace(text) ? "（未发现可用捕获设备）" : text);
        }
        catch
        {
            /* 忽略诊断失败 */
        }
    }
}
