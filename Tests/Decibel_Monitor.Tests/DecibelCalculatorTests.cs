using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class DecibelCalculatorTests
{
    // ---- LinearToDisplayDb ----

    [Theory]
    [InlineData(1.0, 1.0, 150.0)]      // 0 dBFS = 满幅
    [InlineData(0.0, 1.0, 0.0)]        // 静音
    [InlineData(0.5, 1.0, 143.9794)]   // -6.02 dBFS
    [InlineData(0.1, 100.0, 150.0)]    // 放大后超限，clamp 到上限
    [InlineData(0.001, 1.0, 90.0)]     // -60 dBFS -> 90
    public void LinearToDisplayDb_Should_MapWithinScale(double linear, double magnification, double expected)
    {
        var result = DecibelCalculator.LinearToDisplayDb(linear, magnification);
        Assert.Equal(expected, result, 3);
    }

    [Fact]
    public void LinearToDisplayDb_Should_ClampToZeroForNonPositiveInput()
    {
        Assert.Equal(0.0, DecibelCalculator.LinearToDisplayDb(-0.5, 1.0), 6);
        Assert.Equal(0.0, DecibelCalculator.LinearToDisplayDb(0.0, 1000.0), 6);
    }

    [Fact]
    public void LinearToDisplayDb_Should_RespectMagnificationZero()
    {
        // 放大倍数为 0 时任何输入都应映射为 0
        Assert.Equal(0.0, DecibelCalculator.LinearToDisplayDb(1.0, 0.0), 6);
    }

    // ---- DisplayDbToDbFs ----

    [Theory]
    [InlineData(150.0, 0.0)]
    [InlineData(70.0, -80.0)]
    [InlineData(0.0, -150.0)]
    public void DisplayDbToDbFs_Should_ConvertToDbFs(double displayDb, double expectedDbFs)
    {
        Assert.Equal(expectedDbFs, DecibelCalculator.DisplayDbToDbFs(displayDb), 6);
    }

    // ---- DbFsToLinear ----

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-80.0, 0.0001)]
    [InlineData(-60.0, 0.001)]
    public void DbFsToLinear_Should_ConvertToLinear(double dbfs, double expectedLinear)
    {
        Assert.Equal(expectedLinear, DecibelCalculator.DbFsToLinear(dbfs), 6);
    }

    // ---- CalculateMagnification ----

    [Fact]
    public void CalculateMagnification_Should_ReturnOne_WhenMeasuredMatchesTarget()
    {
        // 目标 70 dB（-80 dBFS）对应的线性值为 0.0001，若测量值恰好为该值则倍数为 1
        var result = DecibelCalculator.CalculateMagnification(70.0, 0.0001);
        Assert.Equal(1.0, result, 6);
    }

    [Fact]
    public void CalculateMagnification_Should_ClampToMax_WhenMeasuredIsZero()
    {
        Assert.Equal(1024.0, DecibelCalculator.CalculateMagnification(70.0, 0.0));
    }

    [Fact]
    public void CalculateMagnification_Should_ReturnReasonableValue()
    {
        // 目标 120 dB（-30 dBFS）对应线性值约 0.031623；测量值 0.001 时需要放大约 31.6 倍
        var result = DecibelCalculator.CalculateMagnification(120.0, 0.001);
        Assert.Equal(31.6228, result, 3);
    }

    [Fact]
    public void CalculateMagnification_Should_ClampToZero_WhenMeasuredExceedsTarget()
    {
        // 测量值远超目标时，放大倍数应趋近 0（不会出现负数）
        var result = DecibelCalculator.CalculateMagnification(70.0, 0.5);
        Assert.True(result >= 0.0);
        Assert.True(result < 0.001);
    }

    // ---- 边界与防御性行为 ----

    [Fact]
    public void LinearToDisplayDb_Should_ClampToScaleMax_WhenAmplifiedBeyondFullScale()
    {
        // 输入超满幅（含放大后超幅）一律 clamp 到刻度上限 150
        Assert.Equal(150.0, DecibelCalculator.LinearToDisplayDb(2.0, 1.0), 6);
        Assert.Equal(150.0, DecibelCalculator.LinearToDisplayDb(1.0, 1024.0), 6);
    }

    [Fact]
    public void LinearToDisplayDb_Should_ClampInfinitiesToScaleRange()
    {
        Assert.Equal(150.0, DecibelCalculator.LinearToDisplayDb(double.PositiveInfinity, 1.0), 6);
        Assert.Equal(0.0, DecibelCalculator.LinearToDisplayDb(double.NegativeInfinity, 1.0), 6);
    }

    [Fact]
    public void LinearToDisplayDb_Should_MapSignalThresholdTo70()
    {
        // 线性阈值 0.0001（-80 dBFS）映射到 70，是"检测到有效信号"的基准点
        Assert.Equal(70.0, DecibelCalculator.LinearToDisplayDb(0.0001, 1.0), 6);
    }

    [Fact]
    public void CalculateMagnification_Should_RespectCustomMaxMagnification()
    {
        Assert.Equal(1.0, DecibelCalculator.CalculateMagnification(70.0, 0.0001, 64.0), 6);
        Assert.Equal(64.0, DecibelCalculator.CalculateMagnification(150.0, 0.0001, 64.0), 6);
    }

    [Fact]
    public void CalculateMagnification_Should_ClampToMax_WhenMeasuredIsNegative()
    {
        // 负的测量值视为"无法测量"，与 0 一样返回上限
        Assert.Equal(1024.0, DecibelCalculator.CalculateMagnification(70.0, -1.0));
    }
}
