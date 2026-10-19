namespace PcMonitor.Core;

/// <summary>
/// LibreHardwareMonitor reports 0 for some sensors until their first real sample (Ryzen CPU
/// temperature, clocks and package power right after start). An exact 0 there is "not ready yet",
/// not a reading, so it must not reach min/max or the sparkline. Real zeros elsewhere
/// (fan 0 RPM, 0 % load) are not passed through these guards.
/// </summary>
public static class SensorGuards
{
    public const string WaitingForFirstSample = "waiting for first sample";

    /// <summary>
    /// CPU temperature, clock or package power: exactly 0 means no sample yet. <paramref name="zeroReason"/>
    /// overrides the reason shown for a 0 (e.g. "needs PawnIO driver" when the driver is missing).
    /// </summary>
    public static Reading CpuValue(double? value, string reasonIfMissing, string zeroReason = WaitingForFirstSample) =>
        value == 0 ? Reading.Unavailable(zeroReason) : Reading.Of(value, reasonIfMissing);

    /// <summary>GPU clock or power: 0 only counts as "not ready" while the GPU temperature is missing too.</summary>
    public static Reading GpuClockOrPower(double? value, bool temperatureMissing, string reasonIfMissing) =>
        value == 0 && temperatureMissing ? Reading.Unavailable(WaitingForFirstSample) : Reading.Of(value, reasonIfMissing);
}

public readonly record struct NamedValue(string Name, double? Value);

/// <param name="Value">The clock in MHz, or null.</param>
/// <param name="Source">Which sensors it came from (for the log).</param>
/// <param name="WaitingForSample">True when the chosen sensors exist but still read 0.</param>
public readonly record struct ClockResult(double? Value, string Source, bool WaitingForSample)
{
    public Reading ToReading(string reasonIfMissing, string zeroReason = SensorGuards.WaitingForFirstSample) =>
        Value.HasValue ? Reading.Of(Value.Value)
        : WaitingForSample ? Reading.Unavailable(zeroReason)
        : Reading.Unavailable(reasonIfMissing);
}

/// <summary>Picks the CPU average and max-core clock from LibreHardwareMonitor's clock sensors.</summary>
public static class CpuClocks
{
    private static bool IsCore(string name) => name.StartsWith("Core #", StringComparison.Ordinal);

    private static bool IsEffective(string name) => IsCore(name) && name.Contains("(Effective)", StringComparison.Ordinal);

    private static ClockResult From(IEnumerable<NamedValue> sensors, string source, Func<IEnumerable<double>, double> combine)
    {
        double?[] values = sensors.Select(s => s.Value).Where(v => v.HasValue && double.IsFinite(v.Value)).ToArray();
        double[] ready = values.Where(v => v > 0).Select(v => v!.Value).ToArray();
        if (ready.Length > 0)
            return new ClockResult(combine(ready), source, false);
        return new ClockResult(null, source, values.Any(v => v == 0));
    }

    /// <summary>
    /// Average clock: mean of "Core #n (Effective)" if present, else "Cores (Average)", else the mean
    /// of "Core #n". Zero readings are skipped (not ready yet).
    /// </summary>
    public static ClockResult Average(IReadOnlyList<NamedValue> clocks)
    {
        NamedValue[] effective = clocks.Where(c => IsEffective(c.Name)).ToArray();
        if (effective.Length > 0)
            return From(effective, "Core #n (Effective) mean", v => v.Average());
        NamedValue[] average = clocks.Where(c => c.Name.Equals("Cores (Average)", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (average.Length > 0)
            return From(average.Take(1), "Cores (Average)", v => v.First());
        NamedValue[] cores = clocks.Where(c => IsCore(c.Name) && !IsEffective(c.Name)).ToArray();
        return From(cores, "Core #n mean", v => v.Average());
    }

    /// <summary>Highest "Core #n" clock (not the effective ones).</summary>
    public static ClockResult MaxCore(IReadOnlyList<NamedValue> clocks) =>
        From(clocks.Where(c => IsCore(c.Name) && !IsEffective(c.Name)), "Core #n max", v => v.Max());
}
