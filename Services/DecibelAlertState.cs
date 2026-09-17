namespace Decibel_Monitor.Services;

/// <summary>
/// 分贝提醒提供方对一次分贝值的评估结果。
/// </summary>
/// <param name="IsActive">当前是否处于"超过阈值"提醒状态（供组件显示提示文字）。</param>
/// <param name="Text">提醒文字（来自通知设置）。</param>
public sealed record DecibelAlertState(bool IsActive, string Text);
