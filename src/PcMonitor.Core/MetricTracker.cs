namespace PcMonitor.Core;

public sealed class MetricHistory
{
    public MetricHistory(double windowSeconds) => History = new HistoryBuffer(windowSeconds);

    public HistoryBuffer History { get; }

    public MinMaxTracker MinMax { get; } = new();
}

/// <summary>Feeds every metric of every snapshot into its own history and min/max.</summary>
public sealed class MetricTracker
{
    private readonly Dictionary<string, MetricHistory> _byKey = new(StringComparer.Ordinal);
    private readonly double _windowSeconds;

    public MetricTracker(double windowSeconds = 60) => _windowSeconds = windowSeconds;

    public static string KeyOf(PartKind part, string metricKey) => $"{part}/{metricKey}";

    public void Record(SystemSnapshot snapshot, double timeSeconds)
    {
        foreach (PartSnapshot part in snapshot.Parts)
        {
            foreach (Metric metric in part.Metrics)
            {
                if (metric.Unit == MetricUnit.Text)
                    continue;
                MetricHistory h = Get(part.Kind, metric.Key);
                h.History.Add(timeSeconds, metric.Reading.Value);
                h.MinMax.Observe(metric.Reading.Value);
            }
        }
    }

    public MetricHistory Get(PartKind part, string metricKey)
    {
        string key = KeyOf(part, metricKey);
        if (!_byKey.TryGetValue(key, out MetricHistory? h))
        {
            h = new MetricHistory(_windowSeconds);
            _byKey[key] = h;
        }
        return h;
    }

    public bool TryGet(PartKind part, string metricKey, out MetricHistory? history) =>
        _byKey.TryGetValue(KeyOf(part, metricKey), out history);

    public void ResetMinMax()
    {
        foreach (MetricHistory h in _byKey.Values)
            h.MinMax.Reset();
    }
}
