using System.Collections.Generic;

namespace Decibel_Monitor.Alerting;

/// <summary>
/// 平均音量判定纯函数（与 UI / 宿主无关，便于单元测试）。
/// 用于把一段时间内的瞬时值平滑成平均值，避免单次尖峰误触发提醒。
/// </summary>
public static class AverageVolumeAlertEvaluator
{
    /// <summary>
    /// 计算窗口内样本的平均值；窗口为空时返回 0。
    /// </summary>
    /// <param name="window">窗口内样本（显示刻度 0..150）。</param>
    public static double CalculateAverage(IReadOnlyList<float> window)
    {
        if (window is null || window.Count == 0) return 0.0;

        double sum = 0.0;
        for (var i = 0; i < window.Count; i++)
        {
            sum += window[i];
        }

        return sum / window.Count;
    }

    /// <summary>
    /// 判定窗口平均值是否超过阈值。
    /// </summary>
    /// <param name="window">窗口内样本（显示刻度 0..150）。</param>
    /// <param name="threshold">阈值（显示刻度）。</param>
    /// <remarks>与 <see cref="DecibelAlertEvaluator"/> 一致，阈值比较为严格大于：等于阈值不触发；窗口为空不触发。</remarks>
    public static bool Evaluate(IReadOnlyList<float> window, double threshold)
    {
        if (window is null || window.Count == 0) return false;
        return CalculateAverage(window) > threshold;
    }
}
