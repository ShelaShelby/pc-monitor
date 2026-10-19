namespace PcMonitor.Core;

/// <summary>Min/max of the real values seen since start (or the last reset). Missing values are ignored.</summary>
public sealed class MinMaxTracker
{
    public double? Min { get; private set; }

    public double? Max { get; private set; }

    public int Samples { get; private set; }

    public void Observe(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return;
        double v = value.Value;
        Min = Min.HasValue ? Math.Min(Min.Value, v) : v;
        Max = Max.HasValue ? Math.Max(Max.Value, v) : v;
        Samples++;
    }

    public void Reset()
    {
        Min = null;
        Max = null;
        Samples = 0;
    }
}
