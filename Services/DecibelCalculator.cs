using System;

namespace Decibel_Monitor.Services;

/// <summary>
/// 分贝换算纯函数工具类（与 UI / 音频设备无关，便于单元测试）。
/// </summary>
/// <remarks>
/// 显示刻度（0..150）与 dBFS 的换算关系：
/// displayDb = 150 + dbfs，即 0 dBFS（满幅）对应 150，-150 dBFS 对应 0。
/// </remarks>
public static class DecibelCalculator
{
    /// <summary>
    /// 显示刻度上限。
    /// </summary>
    public const double DisplayScaleMax = 150.0;

    /// <summary>
    /// 将线性峰值（0..1）按放大倍数放大后映射为显示刻度（0..150）。
    /// </summary>
    /// <param name="linear">线性峰值，范围 0..1。</param>
    /// <param name="magnification">校准放大倍数。</param>
    /// <returns>映射后的显示分贝值（0..150）。</returns>
    public static double LinearToDisplayDb(double linear, double magnification)
    {
        var scaled = Math.Clamp(linear * magnification, 0.0, 1.0);
        if (scaled <= 0.0) return 0.0;
        var dbfs = 20.0 * Math.Log10(scaled);
        return Math.Clamp(DisplayScaleMax + dbfs, 0.0, DisplayScaleMax);
    }

    /// <summary>
    /// 将显示刻度（0..150）转换为 dBFS。
    /// </summary>
    public static double DisplayDbToDbFs(double displayDb)
    {
        return displayDb - DisplayScaleMax;
    }

    /// <summary>
    /// 将 dBFS 转换为线性值（0..1）。
    /// </summary>
    public static double DbFsToLinear(double dbfs)
    {
        return Math.Pow(10.0, dbfs / 20.0);
    }

    /// <summary>
    /// 根据目标显示分贝与测量到的线性峰值计算放大倍数，并做上下限保护。
    /// </summary>
    /// <param name="targetDisplayDb">目标显示分贝（0..150）。</param>
    /// <param name="measuredLinear">测量得到的线性峰值（0..1）。</param>
    /// <param name="maxMagnification">放大倍数上限。</param>
    /// <returns>放大倍数。当测量值为 0（无法测量）时返回上限，避免极端错误值。</returns>
    public static double CalculateMagnification(double targetDisplayDb, double measuredLinear, double maxMagnification = 1024.0)
    {
        if (measuredLinear <= 0.0) return maxMagnification;
        var targetLinear = DbFsToLinear(DisplayDbToDbFs(targetDisplayDb));
        return Math.Clamp(targetLinear / measuredLinear, 0.0, maxMagnification);
    }
}
