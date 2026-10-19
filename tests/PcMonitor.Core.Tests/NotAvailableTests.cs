using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class NotAvailableTests
{
    [Fact]
    public void UnavailableReadingHasNoValueAndAReason()
    {
        Reading r = Reading.Unavailable("needs PawnIO driver");
        Assert.False(r.HasValue);
        Assert.Null(r.Value);
        Assert.Equal("needs PawnIO driver", r.Reason);
    }

    [Fact]
    public void EmptyReasonGetsDefault() => Assert.Equal("not available", Reading.Unavailable("  ").Reason);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidNumbersBecomeUnavailable(double v)
    {
        Reading r = Reading.Of(v);
        Assert.False(r.HasValue);
        Assert.NotNull(r.Reason);
    }

    [Fact]
    public void NullableHelperUsesReason()
    {
        Assert.Equal(5, Reading.Of((double?)5, "x").Value);
        Assert.Equal("not exposed", Reading.Of((double?)null, "not exposed").Reason);
    }

    [Theory]
    [InlineData(MetricUnit.Percent)]
    [InlineData(MetricUnit.Celsius)]
    [InlineData(MetricUnit.Bytes)]
    [InlineData(MetricUnit.BytesPerSecond)]
    public void MissingValueFormatsAsNa(MetricUnit unit)
    {
        FormattedValue f = Formatter.Format(unit, null, TemperatureUnit.Celsius);
        Assert.Equal("n/a", f.Value);
        Assert.Equal("", f.Unit);
    }

    [Fact]
    public void NaIncludesReason()
    {
        var m = new Metric("memtemp", "Memory temp", MetricUnit.Celsius, Reading.Unavailable("not exposed by driver"));
        Assert.Equal("n/a (not exposed by driver)", Formatter.FormatWithReason(m, TemperatureUnit.Celsius));
    }

    [Fact]
    public void TextMetricWithoutTextIsNa()
    {
        var m = new Metric("x", "X", MetricUnit.Text, Reading.Unavailable("none"));
        Assert.Equal("n/a", Formatter.Format(m, TemperatureUnit.Celsius).Value);
    }

    [Fact]
    public void FakeProviderWithoutPawnIoShowsCpuTempAsNaNotAFakeNumber()
    {
        var p = new FakeSensorProvider(pawnIoInstalled: false);
        Assert.Equal(DriverState.Missing, p.PawnIo.State);
        Metric temp = p.Read().Find(PartKind.Cpu)!.Find(MetricKeys.Temperature)!;
        Assert.False(temp.Reading.HasValue);
        Assert.Contains("PawnIO", temp.Reading.Reason);
    }
}
