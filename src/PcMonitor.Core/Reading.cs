namespace PcMonitor.Core;

/// <summary>
/// One sensor value, or the honest absence of one. A missing value always carries
/// a short reason so the UI can show "n/a" with an explanation instead of a fake number.
/// </summary>
public readonly record struct Reading
{
    private Reading(double? value, string? reason)
    {
        Value = value;
        Reason = reason;
    }

    public double? Value { get; }

    /// <summary>Why the value is unavailable. Null when <see cref="HasValue"/> is true.</summary>
    public string? Reason { get; }

    public bool HasValue => Value.HasValue;

    public static Reading Of(double value)
    {
        // NaN/Infinity are never real readings; treat them as unavailable rather than display garbage.
        if (double.IsNaN(value) || double.IsInfinity(value))
            return Unavailable("invalid value from sensor");
        return new Reading(value, null);
    }

    public static Reading Of(double? value, string reasonIfMissing) =>
        value.HasValue ? Of(value.Value) : Unavailable(reasonIfMissing);

    public static Reading Unavailable(string reason) =>
        new(null, string.IsNullOrWhiteSpace(reason) ? "not available" : reason.Trim());

    public override string ToString() => HasValue ? Value!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : $"n/a ({Reason})";
}
