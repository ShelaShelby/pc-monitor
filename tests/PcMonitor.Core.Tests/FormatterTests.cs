using System.Globalization;
using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class FormatterTests
{
    private const TemperatureUnit C = TemperatureUnit.Celsius;

    [Theory]
    [InlineData(MetricUnit.Percent, 45.4, "45 %")]
    [InlineData(MetricUnit.Percent, 99.6, "100 %")]
    [InlineData(MetricUnit.MegaHertz, 4850.4, "4,850 MHz")]
    [InlineData(MetricUnit.Watt, 65.25, "65.3 W")]
    [InlineData(MetricUnit.Watt, 182.4, "182 W")]
    [InlineData(MetricUnit.Rpm, 1234.6, "1,235 RPM")]
    [InlineData(MetricUnit.Hours, 12345, "12,345 h")]
    [InlineData(MetricUnit.Volt, 1.2, "1.200 V")]
    [InlineData(MetricUnit.Celsius, 45.26, "45.3 °C")]
    public void FormatsUnits(MetricUnit unit, double value, string expected) =>
        Assert.Equal(expected, Formatter.Format(unit, value, C).ToString());

    [Fact]
    public void FormatsFahrenheit() =>
        Assert.Equal("122.0 °F", Formatter.Format(MetricUnit.Celsius, 50, TemperatureUnit.Fahrenheit).ToString());

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(16.0 * 1024 * 1024 * 1024, "16.0 GB")]
    [InlineData(2000398934016, "1.82 TB")]
    [InlineData(250.0 * 1024 * 1024, "250 MB")]
    public void FormatsBytesBinary(double bytes, string expected) =>
        Assert.Equal(expected, Formatter.FormatBytes(bytes).ToString());

    [Fact]
    public void FormatsRates() =>
        Assert.Equal("3.50 MB/s", Formatter.Format(MetricUnit.BytesPerSecond, 3.5 * 1024 * 1024, C).ToString());

    [Fact]
    public void FormattingDoesNotDependOnMachineCulture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("4,850 MHz", Formatter.Format(MetricUnit.MegaHertz, 4850, C).ToString());
            Assert.Equal("45.3 °C", Formatter.Format(MetricUnit.Celsius, 45.26, C).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void MinMaxText() =>
        Assert.Equal("min 40.0 °C · max 80.0 °C", Formatter.FormatMinMax(MetricUnit.Celsius, 40, 80, C));

    [Fact]
    public void MinMaxWithoutDataShowsNa() =>
        Assert.Equal("min n/a · max n/a", Formatter.FormatMinMax(MetricUnit.Celsius, null, null, C));
}
