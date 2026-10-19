namespace PcMonitor.Core;

/// <summary>The cards the dashboard shows, in display order.</summary>
public enum PartKind
{
    Cpu,
    Gpu,
    Ram,
    DriveC,
    DriveD,
    Motherboard,
    Network,
}

/// <summary>Which threshold table a temperature metric uses.</summary>
public enum TemperatureZone
{
    None,
    CpuCore,
    GpuEdge,
    GpuHotspot,
    GpuMemory,
    Dimm,
    Nvme,
    Motherboard,
}

/// <summary>One labelled value inside a card.</summary>
public sealed record Metric(
    string Key,
    string Label,
    MetricUnit Unit,
    Reading Reading,
    TemperatureZone Zone = TemperatureZone.None,
    TemperatureThresholds? ThresholdOverride = null,
    string? Text = null);

public sealed record PartSnapshot(
    PartKind Kind,
    string Title,
    string Subtitle,
    IReadOnlyList<Metric> Metrics,
    string? Notice = null)
{
    public Metric? Find(string key) => Metrics.FirstOrDefault(m => m.Key == key);
}

public sealed record SystemSnapshot(DateTimeOffset Timestamp, IReadOnlyList<PartSnapshot> Parts)
{
    public PartSnapshot? Find(PartKind kind) => Parts.FirstOrDefault(p => p.Kind == kind);
}

public sealed record SpecLine(string Label, string Value);

public sealed record PartSpecs(PartKind Kind, IReadOnlyList<SpecLine> Lines);

public enum DriverState
{
    /// <summary>Driver installed and its device can be opened.</summary>
    Ready,
    /// <summary>Driver is not installed.</summary>
    Missing,
    /// <summary>Installed (registry entry found) but the device could not be opened (e.g. reboot pending, not elevated).</summary>
    InstalledNotLoaded,
    /// <summary>The provider does not use the driver (fake provider).</summary>
    NotApplicable,
}

public sealed record DriverStatus(DriverState State, string Detail);
