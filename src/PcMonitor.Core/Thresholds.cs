namespace PcMonitor.Core;

public enum ThresholdLevel
{
    /// <summary>No value: shown neutral, never green.</summary>
    Unknown,
    Ok,
    Warn,
    Critical,
}

/// <summary>Green below <see cref="WarnC"/>, yellow from WarnC, red from <see cref="CriticalC"/>. All in °C.</summary>
public sealed record TemperatureThresholds(double WarnC, double CriticalC, string Source)
{
    public ThresholdLevel Evaluate(double? celsius)
    {
        if (!celsius.HasValue || double.IsNaN(celsius.Value) || double.IsInfinity(celsius.Value))
            return ThresholdLevel.Unknown;
        if (celsius.Value >= CriticalC)
            return ThresholdLevel.Critical;
        if (celsius.Value >= WarnC)
            return ThresholdLevel.Warn;
        return ThresholdLevel.Ok;
    }

    public ThresholdLevel Evaluate(Reading reading) => Evaluate(reading.Value);
}

public static class ThresholdTable
{
    // CPU (Ryzen 5 7500X3D): AMD product page lists "Max. Operating Temperature (Tjmax): 89°C".
    // https://www.amd.com/en/products/processors/desktops/ryzen/7000-series/amd-ryzen-5-7500x3d.html
    // Red at Tjmax, yellow 9 °C below it.
    public static readonly TemperatureThresholds CpuCore =
        new(80, 89, "AMD Ryzen 5 7500X3D spec page: Tjmax 89 °C");

    // GPU junction/hotspot: AMD states junction temperatures up to 110 °C are expected and within spec
    // for Radeon RDNA cards (AMD community blog "Radeon RX 5700 Series: Let's Talk Temperatures", 2019).
    // AMD publishes no separate figure for the RX 9060 XT, so the RDNA statement is used.
    public static readonly TemperatureThresholds GpuHotspot =
        new(95, 110, "AMD: RDNA junction up to 110 °C is within spec");

    // GPU edge: AMD publishes no edge limit; edge typically reads 15-25 °C below junction, so the junction
    // limit is shifted down by 10 °C. Derived value, not a vendor number (noted in REPORT.md).
    public static readonly TemperatureThresholds GpuEdge =
        new(85, 100, "derived from AMD 110 °C junction limit (no edge limit published)");

    // GDDR6: Micron lists max operating temperature 95 °C or 105 °C depending on the part; AMD publishes
    // nothing for the RX 9060 XT memory, so yellow at the lower and red at the higher figure.
    public static readonly TemperatureThresholds GpuMemory =
        new(95, 105, "Micron GDDR6: 95/105 °C max operating, part dependent");

    // DDR5 DIMM (SPD hub sensor): JEDEC JESD79-5 normal operating range Tcase 0-85 °C,
    // extended range up to 95 °C (with doubled refresh).
    public static readonly TemperatureThresholds Dimm =
        new(85, 95, "JEDEC JESD79-5: 85 °C normal, 95 °C extended");

    // NVMe: the NVMe Base Specification has each controller report its own Warning (WCTEMP) and
    // Critical (CCTEMP) Composite Temperature thresholds; those are used when the drive exposes them
    // (see ForNvme). Without them: 70/80 °C, a conservative fallback (not a vendor number).
    public static readonly TemperatureThresholds NvmeFallback =
        new(70, 80, "fallback 70/80 °C (drive did not report WCTEMP/CCTEMP)");

    // Motherboard / SuperIO sensors: board vendors publish no limits for these sensors.
    // Conservative generic values, not a vendor number.
    public static readonly TemperatureThresholds Motherboard =
        new(70, 85, "generic 70/85 °C (board vendors publish no sensor limits)");

    public static TemperatureThresholds? For(TemperatureZone zone) => zone switch
    {
        TemperatureZone.CpuCore => CpuCore,
        TemperatureZone.GpuEdge => GpuEdge,
        TemperatureZone.GpuHotspot => GpuHotspot,
        TemperatureZone.GpuMemory => GpuMemory,
        TemperatureZone.Dimm => Dimm,
        TemperatureZone.Nvme => NvmeFallback,
        TemperatureZone.Motherboard => Motherboard,
        _ => null,
    };

    public static TemperatureThresholds For(Metric metric) =>
        metric.ThresholdOverride ?? For(metric.Zone) ?? Motherboard;

    /// <summary>Thresholds from the drive's own WCTEMP/CCTEMP when both are plausible, else the fallback.</summary>
    public static TemperatureThresholds ForNvme(double? warningC, double? criticalC)
    {
        bool plausible(double? v) => v.HasValue && v.Value > 30 && v.Value < 150;
        if (plausible(warningC) && plausible(criticalC) && criticalC!.Value > warningC!.Value)
            return new TemperatureThresholds(warningC.Value, criticalC.Value, "drive-reported WCTEMP/CCTEMP (NVMe spec)");
        if (plausible(warningC))
            return new TemperatureThresholds(warningC!.Value, warningC.Value + 10, "drive-reported WCTEMP (NVMe spec), red = WCTEMP + 10 °C");
        return NvmeFallback;
    }
}
