using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcMonitor.Core;

public sealed class WindowBounds
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public bool Fullscreen { get; set; }
    /// <summary>Device name of the monitor the window was on (e.g. \\.\DISPLAY2). Informational; bounds decide.</summary>
    public string? Monitor { get; set; }
}

public sealed class AppSettings
{
    public static readonly double[] AllowedIntervals = { 0.5, 1.0, 2.0 };

    public double UpdateIntervalSeconds { get; set; } = 1.0;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public bool AlwaysOnTop { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LayoutMode Layout { get; set; } = LayoutMode.Fit;

    /// <summary>Hide specs and tighten row spacing.</summary>
    public bool Compact { get; set; }

    public WindowBounds? Window { get; set; }

    /// <summary>Snap to the nearest allowed interval (0.5 / 1 / 2 s); anything invalid becomes 1 s.</summary>
    public static double NormalizeInterval(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            return 1.0;
        return AllowedIntervals.OrderBy(a => Math.Abs(a - seconds)).ThenBy(a => a).First();
    }

    public AppSettings Normalized()
    {
        UpdateIntervalSeconds = NormalizeInterval(UpdateIntervalSeconds);
        if (!Enum.IsDefined(TemperatureUnit))
            TemperatureUnit = TemperatureUnit.Celsius;
        if (!Enum.IsDefined(Layout))
            Layout = LayoutMode.Fit;
        return this;
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses settings; corrupt or empty input gives defaults instead of crashing the app.</summary>
    public static AppSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AppSettings();
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalized();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}

public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public double IntersectionArea(ScreenRect o)
    {
        double w = Math.Min(Right, o.Right) - Math.Max(Left, o.Left);
        double h = Math.Min(Bottom, o.Bottom) - Math.Max(Top, o.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}

public static class WindowPlacement
{
    /// <summary>
    /// Returns the saved bounds if enough of the window title area is on a connected monitor,
    /// otherwise null (caller falls back to the default position). Keeps the window on the
    /// monitor it was on when that monitor is still attached.
    /// </summary>
    public static ScreenRect? Restore(WindowBounds? saved, IReadOnlyList<ScreenRect> workAreas, double minWidth = 400, double minHeight = 300)
    {
        if (saved is null || workAreas.Count == 0)
            return null;
        double[] values = { saved.Left, saved.Top, saved.Width, saved.Height };
        if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            return null;
        double width = Math.Max(minWidth, saved.Width);
        double height = Math.Max(minHeight, saved.Height);
        var rect = new ScreenRect(saved.Left, saved.Top, width, height);

        // The top strip (title bar) must be reachable, otherwise the window cannot be moved back.
        var titleStrip = new ScreenRect(rect.Left, rect.Top, rect.Width, Math.Min(40, rect.Height));
        ScreenRect best = workAreas.OrderByDescending(a => a.IntersectionArea(titleStrip)).First();
        if (best.IntersectionArea(titleStrip) < titleStrip.Width * titleStrip.Height * 0.25)
            return null;

        // Shrink to fit the monitor it is on.
        width = Math.Min(width, best.Width);
        height = Math.Min(height, best.Height);
        double left = Math.Clamp(rect.Left, best.Left, best.Right - width);
        double top = Math.Clamp(rect.Top, best.Top, best.Bottom - height);
        return new ScreenRect(left, top, width, height);
    }
}
