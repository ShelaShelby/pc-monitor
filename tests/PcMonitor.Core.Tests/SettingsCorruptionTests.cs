using System.Globalization;
using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

/// <summary>settings.json as it can be found after a crash, a hand edit or an older/newer version.</summary>
public class SettingsCorruptionTests
{
    private static void AssertDefaults(AppSettings s)
    {
        Assert.Equal(1.0, s.UpdateIntervalSeconds);
        Assert.Equal(TemperatureUnit.Celsius, s.TemperatureUnit);
        Assert.Equal(LayoutMode.Fit, s.Layout);
        Assert.False(s.Compact);
        Assert.False(s.AlwaysOnTop);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("   \r\n\t ")]
    [InlineData("\0\0\0\0\0\0")]
    [InlineData("{")]
    [InlineData("{\"UpdateIntervalSeconds\": 2, \"Temperat")]
    [InlineData("{\"UpdateIntervalSeconds\": \"fast\"}")]
    [InlineData("{\"UpdateIntervalSeconds\": 1e999}")]
    [InlineData("{\"UpdateIntervalSeconds\": -1e999}")]
    [InlineData("{\"TemperatureUnit\": \"Kelvin\"}")]
    [InlineData("{\"Layout\": \"Columns9\"}")]
    [InlineData("{\"Layout\": null}")]
    [InlineData("{\"Window\": 5}")]
    [InlineData("{\"Window\": {\"Left\": \"x\"}}")]
    [InlineData("{\"AlwaysOnTop\": \"yes\"}")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("<xml/>")]
    public void CorruptOrWrongTypedFileGivesDefaults(string json) => AssertDefaults(AppSettings.FromJson(json));

    [Fact]
    public void DeeplyNestedJsonGivesDefaults()
    {
        string json = "{\"Window\":" + new string('[', 5000) + new string(']', 5000) + "}";
        AssertDefaults(AppSettings.FromJson(json));
    }

    [Fact]
    public void EveryTruncationOfAValidFileLoadsWithoutThrowing()
    {
        string full = new AppSettings
        {
            UpdateIntervalSeconds = 0.5,
            TemperatureUnit = TemperatureUnit.Fahrenheit,
            Layout = LayoutMode.Columns4,
            Window = new WindowBounds { Left = -1920, Top = 0, Width = 1600, Height = 1000, Monitor = @"\\.\DISPLAY2" },
        }.ToJson();
        for (int n = 0; n <= full.Length; n++)
        {
            AppSettings s = AppSettings.FromJson(full[..n]);
            Assert.Contains(s.UpdateIntervalSeconds, AppSettings.AllowedIntervals);
            Assert.True(Enum.IsDefined(s.Layout));
            Assert.True(Enum.IsDefined(s.TemperatureUnit));
        }
    }

    [Theory]
    [InlineData("{\"TemperatureUnit\": 7}", TemperatureUnit.Celsius, LayoutMode.Fit)]
    [InlineData("{\"Layout\": -3}", TemperatureUnit.Celsius, LayoutMode.Fit)]
    [InlineData("{\"Layout\": 2, \"TemperatureUnit\": 1}", TemperatureUnit.Fahrenheit, LayoutMode.Columns3)]
    public void OutOfRangeEnumNumbersAreNormalised(string json, TemperatureUnit unit, LayoutMode layout)
    {
        AppSettings s = AppSettings.FromJson(json);
        Assert.Equal(unit, s.TemperatureUnit);
        Assert.Equal(layout, s.Layout);
    }

    [Fact]
    public void UnknownPropertiesAreIgnoredAndKnownOnesKept()
    {
        AppSettings s = AppSettings.FromJson("{\"FutureOption\": {\"a\": [1,2]}, \"Compact\": true, \"UpdateIntervalSeconds\": 2}");
        Assert.True(s.Compact);
        Assert.Equal(2, s.UpdateIntervalSeconds);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("et-EE")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void JsonIsCultureInvariant(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var s = new AppSettings { UpdateIntervalSeconds = 0.5, Window = new WindowBounds { Left = 12.5, Width = 1234.75, Height = 800 } };
            string json = s.ToJson();
            Assert.Contains("\"UpdateIntervalSeconds\": 0.5", json);
            Assert.Contains("\"Width\": 1234.75", json);
            AppSettings back = AppSettings.FromJson(json);
            Assert.Equal(0.5, back.UpdateIntervalSeconds);
            Assert.Equal(1234.75, back.Window!.Width);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---------- window placement with odd saved bounds ----------

    private static readonly ScreenRect Primary = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(-32000, -32000, 160, 28)]           // minimised window position
    [InlineData(1e308, 1e308, 1e308, 1e308)]
    [InlineData(-1e308, 0, 1e308, 500)]
    [InlineData(5000, 5000, 800, 600)]              // second monitor unplugged
    public void OffScreenOrAbsurdBoundsGiveDefaultPosition(double left, double top, double width, double height)
    {
        var saved = new WindowBounds { Left = left, Top = top, Width = width, Height = height };
        ScreenRect? r = WindowPlacement.Restore(saved, new[] { Primary });
        if (r is { } rect)
        {
            // If anything is restored it must lie fully on the monitor.
            Assert.True(rect.Left >= Primary.Left && rect.Right <= Primary.Right + 1e-6);
            Assert.True(rect.Top >= Primary.Top && rect.Bottom <= Primary.Bottom + 1e-6);
        }
        else
            Assert.Null(r);
    }

    [Theory]
    [InlineData(0, 0, -100, -100)]
    [InlineData(100, 100, 0, 0)]
    public void ZeroOrNegativeSizeIsGrownToTheMinimum(double left, double top, double width, double height)
    {
        ScreenRect? r = WindowPlacement.Restore(new WindowBounds { Left = left, Top = top, Width = width, Height = height }, new[] { Primary });
        Assert.NotNull(r);
        Assert.Equal(400, r!.Value.Width);
        Assert.Equal(300, r.Value.Height);
    }

    [Fact]
    public void ResolutionShrankWindowIsClampedOntoTheMonitor()
    {
        // Saved on a 2560x1440 primary; now 1920x1040 work area.
        ScreenRect? r = WindowPlacement.Restore(new WindowBounds { Left = 1000, Top = 400, Width = 1500, Height = 1000 }, new[] { Primary });
        Assert.Equal(new ScreenRect(420, 40, 1500, 1000), r);
    }

    [Fact]
    public void NoMonitorsGivesNull() =>
        Assert.Null(WindowPlacement.Restore(new WindowBounds { Width = 800, Height = 600 }, Array.Empty<ScreenRect>()));
}
