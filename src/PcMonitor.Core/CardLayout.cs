namespace PcMonitor.Core;

/// <summary>Metric keys shared by every provider, so the UI knows what to put in the big numbers.</summary>
public static class MetricKeys
{
    public const string Load = "load";
    public const string Temperature = "temp";
    public const string Hotspot = "hotspot";
    public const string MemoryTemperature = "memtemp";
    public const string CoreClock = "clock";
    public const string MaxCoreClock = "clock_max";
    public const string MemoryClock = "memclock";
    public const string Power = "power";
    public const string Fan = "fan";
    public const string FanPercent = "fan_pct";
    public const string VramUsed = "vram_used";
    public const string VramTotal = "vram_total";
    public const string Used = "used";
    public const string Available = "available";
    public const string Total = "total";
    public const string Free = "free";
    public const string Health = "health";
    public const string PowerOnHours = "poh";
    public const string Activity = "activity";
    public const string ReadRate = "read";
    public const string WriteRate = "write";
    public const string Download = "down";
    public const string Upload = "up";
    public const string TemperatureMax = "temp_max";
    public const string FanMax = "fan_max";
}

/// <summary>Which two metrics a card shows large and as 60-second sparklines.</summary>
public sealed record CardLayout(PartKind Kind, string HeroKey, string? SecondHeroKey)
{
    public static CardLayout For(PartKind kind) => kind switch
    {
        PartKind.Cpu => new(kind, MetricKeys.Load, MetricKeys.Temperature),
        PartKind.Gpu => new(kind, MetricKeys.Load, MetricKeys.Temperature),
        PartKind.Ram => new(kind, MetricKeys.Load, MetricKeys.Temperature),
        PartKind.DriveC or PartKind.DriveD => new(kind, MetricKeys.Activity, MetricKeys.Temperature),
        PartKind.Motherboard => new(kind, MetricKeys.TemperatureMax, MetricKeys.FanMax),
        PartKind.Network => new(kind, MetricKeys.Download, MetricKeys.Upload),
        _ => new(kind, MetricKeys.Load, null),
    };

    public static string TitleOf(PartKind kind) => kind switch
    {
        PartKind.Cpu => "CPU",
        PartKind.Gpu => "GPU",
        PartKind.Ram => "RAM",
        PartKind.DriveC => "Drive C",
        PartKind.DriveD => "Drive D",
        PartKind.Motherboard => "Motherboard / Fans",
        PartKind.Network => "Network",
        _ => kind.ToString(),
    };
}
