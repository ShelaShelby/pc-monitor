using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class ThresholdTests
{
    [Theory]
    [InlineData(45, ThresholdLevel.Ok)]
    [InlineData(79.9, ThresholdLevel.Ok)]
    [InlineData(80, ThresholdLevel.Warn)]
    [InlineData(88.9, ThresholdLevel.Warn)]
    [InlineData(89, ThresholdLevel.Critical)]
    [InlineData(95, ThresholdLevel.Critical)]
    public void CpuUsesRyzenTjmax(double c, ThresholdLevel expected) =>
        Assert.Equal(expected, ThresholdTable.CpuCore.Evaluate(c));

    [Fact]
    public void CpuCriticalIsTjmax89() => Assert.Equal(89, ThresholdTable.CpuCore.CriticalC);

    [Fact]
    public void GpuHotspotCriticalIs110() => Assert.Equal(110, ThresholdTable.GpuHotspot.CriticalC);

    [Fact]
    public void MissingValueIsUnknownNeverGreen()
    {
        Assert.Equal(ThresholdLevel.Unknown, ThresholdTable.CpuCore.Evaluate((double?)null));
        Assert.Equal(ThresholdLevel.Unknown, ThresholdTable.CpuCore.Evaluate(double.NaN));
        Assert.Equal(ThresholdLevel.Unknown, ThresholdTable.CpuCore.Evaluate(Reading.Unavailable("x")));
    }

    [Fact]
    public void EveryZoneHasSaneThresholdsAndSource()
    {
        foreach (TemperatureZone zone in Enum.GetValues<TemperatureZone>())
        {
            TemperatureThresholds? t = ThresholdTable.For(zone);
            if (zone == TemperatureZone.None)
            {
                Assert.Null(t);
                continue;
            }
            Assert.NotNull(t);
            Assert.True(t!.WarnC < t.CriticalC, $"{zone}: warn must be below critical");
            Assert.False(string.IsNullOrWhiteSpace(t.Source));
        }
    }

    [Fact]
    public void NvmeUsesDriveReportedLimits()
    {
        TemperatureThresholds t = ThresholdTable.ForNvme(82, 85);
        Assert.Equal(82, t.WarnC);
        Assert.Equal(85, t.CriticalC);
        Assert.Equal(ThresholdLevel.Warn, t.Evaluate(83));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, 0.0)]
    [InlineData(85.0, 80.0)]
    [InlineData(-273.0, 500.0)]
    public void NvmeFallsBackOnMissingOrImplausibleLimits(double? warn, double? crit)
    {
        TemperatureThresholds t = ThresholdTable.ForNvme(warn, crit);
        if (warn is > 30 and < 150)
            Assert.Equal(warn, t.WarnC);
        else
            Assert.Same(ThresholdTable.NvmeFallback, t);
    }

    [Fact]
    public void MetricOverrideWins()
    {
        var custom = new TemperatureThresholds(10, 20, "test");
        var m = new Metric("temp", "T", MetricUnit.Celsius, Reading.Of(15), TemperatureZone.CpuCore, custom);
        Assert.Same(custom, ThresholdTable.For(m));
    }
}
