using System.Globalization;

namespace PcMonitor.Core;

public readonly record struct FormattedValue(string Value, string Unit)
{
    public override string ToString() => Unit.Length == 0 ? Value : $"{Value} {Unit}";
}

/// <summary>
/// Turns readings into display text. Always culture-invariant so output does not depend on the
/// machine's regional settings.
/// </summary>
public static class Formatter
{
    public const string NotAvailable = "n/a";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static FormattedValue Format(MetricUnit unit, double? value, TemperatureUnit temperatureUnit)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return new FormattedValue(NotAvailable, "");
        double v = value.Value;
        return unit switch
        {
            MetricUnit.Percent => new(v.ToString("0", Inv), "%"),
            MetricUnit.Celsius => FormatTemperature(v, temperatureUnit),
            MetricUnit.MegaHertz => new(v.ToString("#,0", Inv), "MHz"),
            MetricUnit.Watt => new(v.ToString(Math.Abs(v) >= 100 ? "0" : "0.0", Inv), "W"),
            MetricUnit.Rpm => new(v.ToString("#,0", Inv), "RPM"),
            MetricUnit.Bytes => FormatBytes(v),
            MetricUnit.BytesPerSecond => FormatRate(v),
            MetricUnit.Hours => new(v.ToString("#,0", Inv), "h"),
            MetricUnit.Volt => new(v.ToString("0.000", Inv), "V"),
            MetricUnit.Count => new(v.ToString("#,0", Inv), ""),
            _ => new(v.ToString("0.##", Inv), ""),
        };
    }

    public static FormattedValue Format(Metric metric, TemperatureUnit temperatureUnit)
    {
        if (metric.Unit == MetricUnit.Text)
            return new FormattedValue(string.IsNullOrEmpty(metric.Text) ? NotAvailable : metric.Text!, "");
        return Format(metric.Unit, metric.Reading.Value, temperatureUnit);
    }

    /// <summary>"n/a (reason)" for missing readings, the formatted value otherwise.</summary>
    public static string FormatWithReason(Metric metric, TemperatureUnit temperatureUnit)
    {
        if (metric.Unit != MetricUnit.Text && !metric.Reading.HasValue)
            return $"{NotAvailable} ({metric.Reading.Reason})";
        return Format(metric, temperatureUnit).ToString();
    }

    public static FormattedValue FormatTemperature(double celsius, TemperatureUnit unit)
    {
        double shown = UnitConversion.ToDisplayTemperature(celsius, unit);
        return new FormattedValue(shown.ToString("0.0", Inv), unit == TemperatureUnit.Fahrenheit ? "°F" : "°C");
    }

    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Binary multiples (1 KB = 1024 B), labelled like Windows Explorer does.</summary>
    public static FormattedValue FormatBytes(double bytes)
    {
        double a = Math.Abs(bytes);
        int i = 0;
        while (a >= 1024 && i < SizeUnits.Length - 1)
        {
            a /= 1024;
            i++;
        }
        double scaled = Math.Sign(bytes) * a;
        string fmt = i == 0 ? "0" : a >= 100 ? "0" : a >= 10 ? "0.0" : "0.00";
        return new FormattedValue(scaled.ToString(fmt, Inv), SizeUnits[i]);
    }

    public static FormattedValue FormatRate(double bytesPerSecond)
    {
        FormattedValue b = FormatBytes(bytesPerSecond);
        return new FormattedValue(b.Value, b.Unit + "/s");
    }

    /// <summary>"min 40.0 · max 80.0 °C"; the unit is written once when both ends share it.</summary>
    public static string FormatMinMax(MetricUnit unit, double? min, double? max, TemperatureUnit temperatureUnit)
    {
        FormattedValue lo = Format(unit, min, temperatureUnit);
        FormattedValue hi = Format(unit, max, temperatureUnit);
        string loText = lo.Unit.Length == 0 || lo.Unit == hi.Unit ? lo.Value : lo.ToString();
        return $"min {loText} · max {hi}";
    }

    private const char NoBreakSpace = '\u00A0';

    /// <summary>
    /// Min/max text for display: the line may only wrap between the "min …" and "max …" halves,
    /// so a narrow column shows two whole lines instead of clipping.
    /// </summary>
    public static string KeepMinMaxHalvesTogether(string minMax) =>
        string.Join(" · ", minMax.Split(" · ").Select(h => h.Replace(' ', NoBreakSpace)));
}
