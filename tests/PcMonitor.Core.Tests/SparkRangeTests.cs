using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class SparkRangeTests
{
    [Fact]
    public void SmallTemperatureWobbleGetsTenDegrees()
    {
        // 50..52 °C: padded 49.7..52.3 is under 10, so 10 around the midpoint 51.
        (double lo, double hi) = SparkRange.Compute(new[] { 50.0, 51.0, 52.0 }, MetricUnit.Celsius);
        Assert.Equal(46, lo, 9);
        Assert.Equal(56, hi, 9);
    }

    [Fact]
    public void WideRangeKeepsHeadroomOnly()
    {
        // 40..60 °C: 15 % of 20 = 3 each side.
        (double lo, double hi) = SparkRange.Compute(new[] { 40.0, 60.0 }, MetricUnit.Celsius);
        Assert.Equal(37, lo, 9);
        Assert.Equal(63, hi, 9);
    }

    [Theory]
    [InlineData(MetricUnit.Rpm, 1000, 1100, 900, 1200)]          // span 300
    [InlineData(MetricUnit.MegaHertz, 4500, 4600, 4300, 4800)]   // span 500
    [InlineData(MetricUnit.Watt, 30, 31, 25.5, 35.5)]            // span 10
    [InlineData(MetricUnit.Percent, 20, 22, 16, 26)]             // span 10
    [InlineData(MetricUnit.Count, 5, 6, 4.85, 6.15)]             // no minimum: headroom only
    public void MinimumSpanPerUnit(MetricUnit unit, double a, double b, double expectedLo, double expectedHi)
    {
        (double lo, double hi) = SparkRange.Compute(new[] { a, b }, unit);
        Assert.Equal(expectedLo, lo, 9);
        Assert.Equal(expectedHi, hi, 9);
    }

    [Fact]
    public void BytesAndRates()
    {
        double gib = UnitConversion.BytesPerGiB, mib = UnitConversion.BytesPerMiB;
        (double lo, double hi) = SparkRange.Compute(new[] { 10 * gib, 10.2 * gib }, MetricUnit.Bytes);
        Assert.Equal(1 * gib, hi - lo, 3);
        Assert.Equal(10.1 * gib, (lo + hi) / 2, 3);
        (lo, hi) = SparkRange.Compute(new[] { 0.0, 100_000 }, MetricUnit.BytesPerSecond);
        Assert.Equal(1 * mib, hi - lo, 3);
    }

    [Fact]
    public void FixedRangeIsKept()
    {
        (double lo, double hi) = SparkRange.Compute(new[] { 20.0, 22.0 }, MetricUnit.Percent, 0, 100);
        Assert.Equal(0, lo);
        Assert.Equal(100, hi);
    }
}
