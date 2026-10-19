using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class SettingsTests
{
    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(0.7, 0.5)]
    [InlineData(1.6, 2)]
    [InlineData(10, 2)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(double.NaN, 1)]
    public void IntervalSnapsToAllowedValues(double input, double expected) =>
        Assert.Equal(expected, AppSettings.NormalizeInterval(input));

    [Fact]
    public void RoundTripsThroughJson()
    {
        var s = new AppSettings
        {
            UpdateIntervalSeconds = 2,
            TemperatureUnit = TemperatureUnit.Fahrenheit,
            AlwaysOnTop = true,
            Window = new WindowBounds { Left = 1920, Top = 10, Width = 1200, Height = 900, Monitor = @"\\.\DISPLAY2" },
        };
        AppSettings back = AppSettings.FromJson(s.ToJson());
        Assert.Equal(2, back.UpdateIntervalSeconds);
        Assert.Equal(TemperatureUnit.Fahrenheit, back.TemperatureUnit);
        Assert.True(back.AlwaysOnTop);
        Assert.Equal(1920, back.Window!.Left);
        Assert.Equal(@"\\.\DISPLAY2", back.Window.Monitor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[]")]
    public void CorruptJsonGivesDefaults(string? json)
    {
        AppSettings s = AppSettings.FromJson(json);
        Assert.Equal(1.0, s.UpdateIntervalSeconds);
        Assert.Equal(TemperatureUnit.Celsius, s.TemperatureUnit);
    }

    private static readonly ScreenRect Primary = new(0, 0, 1920, 1040);
    private static readonly ScreenRect Second = new(1920, 0, 2560, 1400);

    [Fact]
    public void RestoresOnSecondMonitor()
    {
        var saved = new WindowBounds { Left = 2000, Top = 50, Width = 1600, Height = 1000 };
        ScreenRect? r = WindowPlacement.Restore(saved, new[] { Primary, Second });
        Assert.Equal(new ScreenRect(2000, 50, 1600, 1000), r);
    }

    [Fact]
    public void MonitorGoneFallsBackToDefault()
    {
        var saved = new WindowBounds { Left = 2000, Top = 50, Width = 1600, Height = 1000 };
        Assert.Null(WindowPlacement.Restore(saved, new[] { Primary }));
    }

    [Fact]
    public void TooLargeWindowIsShrunkToMonitor()
    {
        var saved = new WindowBounds { Left = 1920, Top = 0, Width = 5000, Height = 3000 };
        ScreenRect? r = WindowPlacement.Restore(saved, new[] { Primary, Second });
        Assert.Equal(new ScreenRect(1920, 0, 2560, 1400), r);
    }

    [Fact]
    public void NoSavedBoundsGivesNull() => Assert.Null(WindowPlacement.Restore(null, new[] { Primary }));
}
