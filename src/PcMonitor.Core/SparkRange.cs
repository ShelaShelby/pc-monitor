namespace PcMonitor.Core;

/// <summary>Vertical range of a sparkline.</summary>
public static class SparkRange
{
    /// <summary>
    /// Smallest span the auto range shows, so small wobbles (a 2 °C change) do not fill the full height.
    /// 0 means no minimum. Temperatures are stored in °C whatever the display unit.
    /// </summary>
    public static double MinimumSpan(MetricUnit unit) => unit switch
    {
        MetricUnit.Celsius => 10,
        MetricUnit.Percent => 10,
        MetricUnit.MegaHertz => 500,
        MetricUnit.Watt => 10,
        MetricUnit.Rpm => 300,
        MetricUnit.Bytes => UnitConversion.BytesPerGiB,
        MetricUnit.BytesPerSecond => UnitConversion.BytesPerMiB,
        _ => 0,
    };

    /// <summary>
    /// Range for the values. A fixed bound (not NaN) is kept as is. The auto side(s) get 15 %
    /// headroom, and the range then grows around the data midpoint to at least <see cref="MinimumSpan"/>.
    /// </summary>
    public static (double Min, double Max) Compute(IReadOnlyList<double> values, MetricUnit unit, double fixedMin = double.NaN, double fixedMax = double.NaN)
    {
        bool autoMin = double.IsNaN(fixedMin), autoMax = double.IsNaN(fixedMax);
        double[] v = values.Where(double.IsFinite).ToArray();
        if (v.Length == 0)
            return (autoMin ? 0 : fixedMin, autoMax ? (autoMin ? 1 : fixedMin + 1) : fixedMax);

        double dataLo = v.Min(), dataHi = v.Max();
        double lo = autoMin ? dataLo : fixedMin;
        double hi = autoMax ? dataHi : fixedMax;
        if (autoMin || autoMax)
        {
            // Headroom; a flat line sits in the middle.
            double span = dataHi - dataLo;
            double pad = span < 1e-9 ? Math.Max(1, Math.Abs(dataHi) * 0.1) : span * 0.15;
            if (autoMin) lo -= pad;
            if (autoMax) hi += pad;

            double minSpan = MinimumSpan(unit);
            if (hi - lo < minSpan)
            {
                if (autoMin && autoMax)
                {
                    double mid = (dataLo + dataHi) / 2;
                    lo = mid - minSpan / 2;
                    hi = mid + minSpan / 2;
                }
                else if (autoMin)
                    lo = hi - minSpan;
                else
                    hi = lo + minSpan;
            }
        }
        if (hi - lo < 1e-9)
            hi = lo + 1;
        return (lo, hi);
    }
}
