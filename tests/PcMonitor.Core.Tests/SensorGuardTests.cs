using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class SensorGuardTests
{
    [Fact]
    public void CpuZeroIsWaitingForFirstSample()
    {
        Reading r = SensorGuards.CpuValue(0, "needs PawnIO driver");
        Assert.False(r.HasValue);
        Assert.Equal("waiting for first sample", r.Reason);
    }

    [Fact]
    public void CpuRealAndMissingValuesPassThrough()
    {
        Assert.Equal(54.5, SensorGuards.CpuValue(54.5, "x").Value);
        Assert.Equal("needs PawnIO driver", SensorGuards.CpuValue(null, "needs PawnIO driver").Reason);
    }

    [Fact]
    public void ZeroWithoutDriverKeepsTheDriverReason() =>
        Assert.Equal("needs PawnIO driver", SensorGuards.CpuValue(0, "needs PawnIO driver", "needs PawnIO driver").Reason);

    [Fact]
    public void GpuZeroOnlyHiddenWhileTemperatureIsMissing()
    {
        Assert.Equal(SensorGuards.WaitingForFirstSample, SensorGuards.GpuClockOrPower(0, temperatureMissing: true, "x").Reason);
        Assert.Equal(0, SensorGuards.GpuClockOrPower(0, temperatureMissing: false, "x").Value);
    }

    [Fact]
    public void ZerosAreNotRecordedInMinMaxOrHistory()
    {
        var tracker = new MetricTracker();
        var metric = new Metric(MetricKeys.Temperature, "Tctl/Tdie", MetricUnit.Celsius, SensorGuards.CpuValue(0, "x"));
        var snapshot = new SystemSnapshot(DateTimeOffset.UnixEpoch, new[] { new PartSnapshot(PartKind.Cpu, "CPU", "", new[] { metric }) });
        tracker.Record(snapshot, 0);
        MetricHistory h = tracker.Get(PartKind.Cpu, MetricKeys.Temperature);
        Assert.Null(h.MinMax.Min);
        Assert.Null(h.History.Snapshot().Single().Value);
    }

    private static readonly NamedValue[] RyzenClocks =
    {
        new("Bus Speed", 100),
        new("Cores (Average)", 4541),
        new("Core #1", 4541), new("Core #2", 4541),
        new("Core #1 (Effective)", 3000), new("Core #2 (Effective)", 1000),
    };

    [Fact]
    public void AveragePrefersEffectiveClocks()
    {
        ClockResult avg = CpuClocks.Average(RyzenClocks);
        Assert.Equal(2000, avg.Value);
        Assert.Equal(4541, CpuClocks.MaxCore(RyzenClocks).Value);
    }

    [Fact]
    public void AverageFallsBackToCoresAverageThenCoreMean()
    {
        NamedValue[] noEffective = RyzenClocks.Where(c => !c.Name.Contains("Effective")).ToArray();
        Assert.Equal("Cores (Average)", CpuClocks.Average(noEffective).Source);
        NamedValue[] coresOnly = { new("Core #1", 4000), new("Core #2", 4600) };
        Assert.Equal(4300, CpuClocks.Average(coresOnly).Value);
    }

    [Fact]
    public void ZeroClocksAreWaiting()
    {
        NamedValue[] zeros = { new("Core #1", 0), new("Core #2", 0) };
        ClockResult max = CpuClocks.MaxCore(zeros);
        Assert.Null(max.Value);
        Assert.Equal(SensorGuards.WaitingForFirstSample, max.ToReading("needs PawnIO driver").Reason);
        NamedValue[] missing = { new("Core #1", null) };
        Assert.Equal("needs PawnIO driver", CpuClocks.MaxCore(missing).ToReading("needs PawnIO driver").Reason);
    }
}
