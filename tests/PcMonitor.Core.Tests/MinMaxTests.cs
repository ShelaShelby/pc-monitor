using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class MinMaxTests
{
    [Fact]
    public void StartsEmpty()
    {
        var m = new MinMaxTracker();
        Assert.Null(m.Min);
        Assert.Null(m.Max);
        Assert.Equal(0, m.Samples);
    }

    [Fact]
    public void TracksMinAndMax()
    {
        var m = new MinMaxTracker();
        foreach (double v in new[] { 50.0, 42.5, 77.0, 60.0 })
            m.Observe(v);
        Assert.Equal(42.5, m.Min);
        Assert.Equal(77.0, m.Max);
        Assert.Equal(4, m.Samples);
    }

    [Fact]
    public void IgnoresMissingAndInvalid()
    {
        var m = new MinMaxTracker();
        m.Observe(null);
        m.Observe(double.NaN);
        m.Observe(double.PositiveInfinity);
        Assert.Null(m.Min);
        m.Observe(-5);
        m.Observe(null);
        Assert.Equal(-5, m.Min);
        Assert.Equal(-5, m.Max);
        Assert.Equal(1, m.Samples);
    }

    [Fact]
    public void ResetClears()
    {
        var m = new MinMaxTracker();
        m.Observe(3);
        m.Reset();
        Assert.Null(m.Max);
        Assert.Equal(0, m.Samples);
    }

    [Fact]
    public void MetricTrackerRecordsEveryNumericMetric()
    {
        var provider = new FakeSensorProvider(seed: 1);
        var tracker = new MetricTracker();
        for (int i = 0; i < 5; i++)
            tracker.Record(provider.Read(), i);

        MetricHistory cpuLoad = tracker.Get(PartKind.Cpu, MetricKeys.Load);
        Assert.Equal(5, cpuLoad.History.Count);
        Assert.Equal(5, cpuLoad.MinMax.Samples);
        Assert.True(cpuLoad.MinMax.Min <= cpuLoad.MinMax.Max);

        // The simulated GPU memory temperature is n/a: gaps in history, no min/max invented.
        MetricHistory memTemp = tracker.Get(PartKind.Gpu, MetricKeys.MemoryTemperature);
        Assert.Equal(5, memTemp.History.Count);
        Assert.All(memTemp.History.Snapshot(), p => Assert.Null(p.Value));
        Assert.Null(memTemp.MinMax.Max);
    }
}
