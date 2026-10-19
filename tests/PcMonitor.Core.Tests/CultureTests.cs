using System.Globalization;
using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

/// <summary>Display text must not depend on Windows regional settings (Alex runs Estonian: decimal comma, space grouping).</summary>
public class CultureTests
{
    public static readonly TheoryData<string> Cultures = new() { "en-US", "et-EE", "de-DE", "fr-FR", "tr-TR" };

    private static T Under<T>(string culture, Func<T> f)
    {
        CultureInfo savedC = CultureInfo.CurrentCulture, savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return f();
        }
        finally
        {
            CultureInfo.CurrentCulture = savedC;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    // Expected strings written by hand (invariant: '.' decimal, ',' thousands).
    private static readonly (MetricUnit Unit, double Value, string Text)[] Cases =
    {
        (MetricUnit.Percent, 42.4, "42 %"),
        (MetricUnit.Celsius, 61.25, "61.3 °C"),   // 61.25 is exact in binary; .NET rounds half away from zero
        (MetricUnit.MegaHertz, 4450.4, "4,450 MHz"),
        (MetricUnit.Watt, 87.25, "87.3 W"),
        (MetricUnit.Watt, 154.6, "155 W"),
        (MetricUnit.Rpm, 1234, "1,234 RPM"),
        (MetricUnit.Bytes, 16 * UnitConversion.BytesPerGiB, "16.0 GB"),
        (MetricUnit.Bytes, 1.5 * UnitConversion.BytesPerKiB, "1.50 KB"),
        (MetricUnit.BytesPerSecond, 2.5 * UnitConversion.BytesPerMiB, "2.50 MB/s"),
        (MetricUnit.Hours, 12345, "12,345 h"),
        (MetricUnit.Volt, 1.1, "1.100 V"),
        (MetricUnit.Count, 1000000, "1,000,000"),
    };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void FormattingIsTheSameInEveryCulture(string culture)
    {
        foreach ((MetricUnit unit, double value, string text) in Cases)
            Assert.Equal(text, Under(culture, () => Formatter.Format(unit, value, TemperatureUnit.Celsius).ToString()));
        Assert.Equal("142.5 °F", Under(culture, () => Formatter.Format(MetricUnit.Celsius, 61.39, TemperatureUnit.Fahrenheit).ToString()));
        Assert.Equal("min 40.0 · max 80.5 °C", Under(culture, () => Formatter.FormatMinMax(MetricUnit.Celsius, 40, 80.5, TemperatureUnit.Celsius)));
        Assert.Equal("n/a (needs PawnIO driver)", Under(culture, () =>
            Formatter.FormatWithReason(new Metric("t", "T", MetricUnit.Celsius, Reading.Unavailable("needs PawnIO driver")), TemperatureUnit.Celsius)));
        Assert.Equal("42.5", Under(culture, () => Reading.Of(42.5).ToString()));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void IntervalNormalisingIsCultureFree(string culture) =>
        Assert.Equal(0.5, Under(culture, () => AppSettings.FromJson("{\"UpdateIntervalSeconds\": 0.5}").UpdateIntervalSeconds));

    [Fact]
    public void TurkishIDoesNotBreakKeysOrLayoutNames()
    {
        // "Fit"/"fit" and metric keys are compared ordinally/invariantly, never with the Turkish dotless i rules.
        Assert.Equal(LayoutMode.Fit, Under("tr-TR", () => AppSettings.FromJson("{\"Layout\": \"Fit\"}").Layout));
        Assert.Equal(LayoutMode.Fit, Under("tr-TR", () => AppSettings.FromJson("{\"Layout\": \"fit\"}").Layout));
        Assert.Equal("Drive C", Under("tr-TR", () => CardLayout.TitleOf(PartKind.DriveC)));
        Assert.Equal("Cpu/load", Under("tr-TR", () => MetricTracker.KeyOf(PartKind.Cpu, MetricKeys.Load)));
        Assert.True(Under("tr-TR", () => GpuPicker.IsIntegrated(new GpuCandidate("AMD RADEON(TM) GRAPHICS", GpuVendor.Amd, null))));
        Assert.True(Under("tr-TR", () => GpuPicker.IsIntegrated(new GpuCandidate("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, null))));
    }
}
