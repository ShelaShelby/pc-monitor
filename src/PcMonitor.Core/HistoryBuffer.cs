namespace PcMonitor.Core;

public readonly record struct HistoryPoint(double TimeSeconds, double? Value);

/// <summary>
/// Keeps the samples of the last <see cref="WindowSeconds"/> seconds (time based, so it is
/// independent of the update rate). Missing samples are stored as gaps (null), never invented.
/// Time is a monotonic seconds counter supplied by the caller.
/// </summary>
public sealed class HistoryBuffer
{
    private readonly LinkedList<HistoryPoint> _points = new();

    public HistoryBuffer(double windowSeconds = 60, int maxPoints = 2000)
    {
        if (windowSeconds <= 0 || double.IsNaN(windowSeconds) || double.IsInfinity(windowSeconds))
            throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        if (maxPoints < 2)
            throw new ArgumentOutOfRangeException(nameof(maxPoints));
        WindowSeconds = windowSeconds;
        MaxPoints = maxPoints;
    }

    public double WindowSeconds { get; }

    public int MaxPoints { get; }

    public int Count => _points.Count;

    public double? LatestTime => _points.Last?.Value.TimeSeconds;

    public void Add(double timeSeconds, double? value)
    {
        if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds))
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value)))
            value = null;

        // A clock that runs backwards means a new time base: old samples cannot be placed, drop them.
        if (_points.Last is { } last && timeSeconds < last.Value.TimeSeconds)
            _points.Clear();

        _points.AddLast(new HistoryPoint(timeSeconds, value));

        double cutoff = timeSeconds - WindowSeconds;
        while (_points.First is { } first && first.Value.TimeSeconds < cutoff)
            _points.RemoveFirst();
        while (_points.Count > MaxPoints)
            _points.RemoveFirst();
    }

    public void Clear() => _points.Clear();

    public IReadOnlyList<HistoryPoint> Snapshot() => _points.ToArray();
}
