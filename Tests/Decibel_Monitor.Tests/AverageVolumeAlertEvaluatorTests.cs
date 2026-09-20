using System.Collections.Generic;
using Decibel_Monitor.Alerting;

namespace Decibel_Monitor.Tests;

public class AverageVolumeAlertEvaluatorTests
{
    private const double Threshold = 100.0;

    // ---- 平均值计算 ----

    [Fact]
    public void CalculateAverage_Should_ReturnZero_WhenWindowEmpty()
    {
        Assert.Equal(0.0, AverageVolumeAlertEvaluator.CalculateAverage(new List<float>()));
    }

    [Fact]
    public void CalculateAverage_Should_ReturnMean_WhenWindowHasSamples()
    {
        var window = new List<float> { 80f, 100f, 120f };

        Assert.Equal(100.0, AverageVolumeAlertEvaluator.CalculateAverage(window), 6);
    }

    // ---- 超阈值判定 ----

    [Fact]
    public void Evaluate_Should_ReturnFalse_WhenWindowEmpty()
    {
        Assert.False(AverageVolumeAlertEvaluator.Evaluate(new List<float>(), Threshold));
    }

    [Fact]
    public void Evaluate_Should_ReturnFalse_WhenBelowThreshold()
    {
        var window = new List<float> { 80f, 90f, 99f };

        Assert.False(AverageVolumeAlertEvaluator.Evaluate(window, Threshold));
    }

    [Fact]
    public void Evaluate_Should_ReturnFalse_WhenAverageEqualsThreshold()
    {
        // 边界：严格大于才算超阈值
        var window = new List<float> { 90f, 100f, 110f };

        Assert.False(AverageVolumeAlertEvaluator.Evaluate(window, Threshold));
    }

    [Fact]
    public void Evaluate_Should_ReturnTrue_WhenAverageAboveThreshold()
    {
        var window = new List<float> { 100f, 120f, 130f };

        Assert.True(AverageVolumeAlertEvaluator.Evaluate(window, Threshold));
    }

    [Fact]
    public void Evaluate_Should_SmoothSinglePeak_ByWindowAverage()
    {
        // 单次尖峰经窗口平均后不应超阈值：这是平均判定相对瞬时判定的意义所在
        var window = new List<float> { 60f, 60f, 60f, 150f };

        Assert.Equal(82.5, AverageVolumeAlertEvaluator.CalculateAverage(window), 6);
        Assert.False(AverageVolumeAlertEvaluator.Evaluate(window, Threshold));
    }
}
