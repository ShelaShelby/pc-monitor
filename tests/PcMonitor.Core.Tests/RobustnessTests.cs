using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

/// <summary>Fuzzes the Core pipeline (guard → tracker → formatter → sparkline range → thresholds) with hostile sensor data.</summary>
public class RobustnessTests
{
    private static readonly double[] Hostile =
    {
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, -273.15, 0, -0.0, 1e308, -1e308,
        double.Epsilon, double.MaxValue, double.MinValue, 255, -128, 1e-300, 42.5,
    };

    private static readonly MetricUnit[] Units = Enum.GetValues<MetricUnit>();

    /// <summary>Random snapshot: parts and sensors come and go, duplicate parts and keys, hostile values.</summary>
    private static SystemSnapshot RandomSnapshot(Random rng, int tick)
    {
        var parts = new List<PartSnapshot>();
        foreach (PartKind kind in Enum.GetValues<PartKind>())
        {
            int copies = rng.Next(10) switch { 0 => 0, 1 => 2, _ => 1 };   // missing, duplicated, normal
            for (int c = 0; c < copies; c++)
            {
                var metrics = new List<Metric>();
                int n = rng.Next(0, 8);
                for (int i = 0; i < n; i++)
                {
                    // Few distinct keys so duplicates within a part are common.
                    string key = rng.Next(4) == 0 ? CardLayout.For(kind).HeroKey : $"k{rng.Next(5)}";
                    MetricUnit unit = Units[rng.Next(Units.Length)];
                    Reading reading = rng.Next(5) switch
                    {
                        0 => Reading.Unavailable(rng.Next(2) == 0 ? "" : "gone"),
                        1 => Reading.Of(Hostile[rng.Next(Hostile.Length)]),
                        _ => Reading.Of(rng.NextDouble() * 200 - 50),
                    };
                    metrics.Add(new Metric(key, $"label {i}", unit, reading,
                        (TemperatureZone)rng.Next(Enum.GetValues<TemperatureZone>().Length),
                        Text: unit == MetricUnit.Text && rng.Next(2) == 0 ? null : "txt"));
                }
                parts.Add(new PartSnapshot(kind, kind.ToString(), "", metrics, rng.Next(6) == 0 ? "notice" : null));
            }
        }
        return new SystemSnapshot(DateTimeOffset.UnixEpoch.AddSeconds(tick), parts);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7500)]
    [InlineData(90601)]
    public void HostileSnapshotsNeverThrowOrPoisonMinMax(int seed)
    {
        var rng = new Random(seed);
        var tracker = new MetricTracker(60);
        // Reference min/max of the finite values per key, computed independently.
        var expected = new Dictionary<string, (double Min, double Max)>();
        for (int tick = 0; tick < 3000; tick++)
        {
            SystemSnapshot snap = SnapshotGuard.Sanitize(RandomSnapshot(rng, tick));
            Assert.Equal(snap.Parts.Count, snap.Parts.Select(p => p.Kind).Distinct().Count());
            foreach (PartSnapshot p in snap.Parts)
                Assert.Equal(p.Metrics.Count, p.Metrics.Select(m => m.Key).Distinct().Count());

            double t = tick * 0.5;
            tracker.Record(snap, t);
            if (tick % 1000 == 999)
            {
                tracker.ResetMinMax();
                expected.Clear();
            }
            else
            {
                foreach (PartSnapshot p in snap.Parts)
                    foreach (Metric m in p.Metrics.Where(m => m.Unit != MetricUnit.Text && m.Reading.HasValue))
                    {
                        string k = MetricTracker.KeyOf(p.Kind, m.Key);
                        double v = m.Reading.Value!.Value;
                        expected[k] = expected.TryGetValue(k, out var mm) ? (Math.Min(mm.Min, v), Math.Max(mm.Max, v)) : (v, v);
                    }
            }

            foreach (PartSnapshot p in snap.Parts)
            {
                foreach (Metric m in p.Metrics)
                {
                    foreach (TemperatureUnit tu in Enum.GetValues<TemperatureUnit>())
                    {
                        string text = Formatter.FormatWithReason(m, tu);
                        Assert.DoesNotContain("NaN", text);
                        Assert.DoesNotContain("∞", text);
                        Assert.DoesNotContain("Infinity", text);
                        ThresholdTable.For(m).Evaluate(m.Reading);
                    }
                    if (m.Unit == MetricUnit.Text)
                        continue;
                    Assert.True(tracker.TryGet(p.Kind, m.Key, out MetricHistory? h));
                    MinMaxTracker mm = h!.MinMax;
                    if (mm.Min is double lo)
                    {
                        Assert.True(double.IsFinite(lo) && double.IsFinite(mm.Max!.Value) && lo <= mm.Max.Value);
                        (double eMin, double eMax) = expected[MetricTracker.KeyOf(p.Kind, m.Key)];
                        Assert.Equal(eMin, lo);
                        Assert.Equal(eMax, mm.Max.Value);
                    }
                    string minMax = Formatter.FormatMinMax(m.Unit, mm.Min, mm.Max, TemperatureUnit.Fahrenheit);
                    Assert.DoesNotContain("NaN", minMax);

                    IReadOnlyList<HistoryPoint> pts = h.History.Snapshot();
                    Assert.True(pts.Count <= h.History.MaxPoints);
                    Assert.All(pts, pt => Assert.True(pt.TimeSeconds >= t - 60 - 1e-9 && pt.TimeSeconds <= t));
                    double[] values = pts.Where(x => x.Value.HasValue).Select(x => x.Value!.Value).ToArray();
                    Assert.All(values, v => Assert.True(double.IsFinite(v)));
                    (double rLo, double rHi) = SparkRange.Compute(values, m.Unit);
                    Assert.True(double.IsFinite(rLo) || values.Any(v => Math.Abs(v) > 1e300), $"range lo {rLo}");
                    Assert.True(rHi > rLo || values.Any(v => Math.Abs(v) > 1e300), $"range {rLo}..{rHi}");
                }
            }
        }
    }

    [Fact]
    public void SparkRangeIsFiniteForEveryFiniteNormalValue()
    {
        var rng = new Random(3);
        for (int i = 0; i < 5000; i++)
        {
            double[] v = Enumerable.Range(0, rng.Next(1, 30)).Select(_ => (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(0, 13))).ToArray();
            foreach (MetricUnit unit in Units)
            {
                (double lo, double hi) = SparkRange.Compute(v, unit);
                Assert.True(double.IsFinite(lo) && double.IsFinite(hi) && hi > lo);
                Assert.True(lo <= v.Min() && hi >= v.Max());
            }
        }
    }

    // ---------- sensors that disappear and come back ----------

    private static SystemSnapshot Snap(params PartSnapshot[] parts) => new(DateTimeOffset.UnixEpoch, parts);

    private static PartSnapshot Drive(double? temp) => new(PartKind.DriveD, "Drive D", "D:", new[]
    {
        new Metric(MetricKeys.Activity, "Activity", MetricUnit.Percent, Reading.Of(5)),
        new Metric(MetricKeys.Temperature, "Temperature", MetricUnit.Celsius, temp is double t ? Reading.Of(t) : Reading.Unavailable("gone")),
    });

    [Fact]
    public void MissingPartLeavesGapsSoItsSparklineAgesOut()
    {
        var tracker = new MetricTracker(60);
        for (int i = 0; i < 10; i++)
            tracker.Record(Snap(Drive(40 + i)), i);
        // Drive D unplugged: the part is not in the snapshot any more.
        for (int i = 10; i < 80; i++)
            tracker.Record(Snap(), i);
        IReadOnlyList<HistoryPoint> pts = tracker.Get(PartKind.DriveD, MetricKeys.Temperature).History.Snapshot();
        Assert.NotEmpty(pts);
        Assert.Equal(79, pts[^1].TimeSeconds);
        Assert.All(pts, p => Assert.Null(p.Value));
        // Min/max since start is kept (it is history, not a current value).
        Assert.Equal(40, tracker.Get(PartKind.DriveD, MetricKeys.Temperature).MinMax.Min);
        Assert.Equal(49, tracker.Get(PartKind.DriveD, MetricKeys.Temperature).MinMax.Max);

        // Plugged back in: values continue after the gap.
        tracker.Record(Snap(Drive(55)), 80);
        IReadOnlyList<HistoryPoint> back = tracker.Get(PartKind.DriveD, MetricKeys.Temperature).History.Snapshot();
        Assert.Equal(55, back[^1].Value);
        Assert.Equal(55, tracker.Get(PartKind.DriveD, MetricKeys.Temperature).MinMax.Max);
    }

    [Fact]
    public void FlappingSensorRecordsGapsNotStaleValues()
    {
        var tracker = new MetricTracker(60);
        for (int i = 0; i < 20; i++)
            tracker.Record(Snap(Drive(i % 2 == 0 ? 50 : null)), i);
        IReadOnlyList<HistoryPoint> pts = tracker.Get(PartKind.DriveD, MetricKeys.Temperature).History.Snapshot();
        Assert.Equal(20, pts.Count);
        for (int i = 0; i < 20; i++)
            Assert.Equal(i % 2 == 0 ? 50 : null, pts[i].Value);
    }

    [Fact]
    public void DuplicateSensorNamesKeepSeparateMinMax()
    {
        var part = new PartSnapshot(PartKind.Motherboard, "MB", "", new[]
        {
            new Metric("f:CPU Fan", "CPU fan", MetricUnit.Rpm, Reading.Of(900)),
            new Metric("f:CPU Fan", "CPU fan", MetricUnit.Rpm, Reading.Of(2400)),
            new Metric("f:CPU Fan", "CPU fan", MetricUnit.Rpm, Reading.Of(0)),
        });
        SystemSnapshot clean = SnapshotGuard.Sanitize(Snap(part));
        Assert.Equal(new[] { "f:CPU Fan", "f:CPU Fan#2", "f:CPU Fan#3" }, clean.Parts[0].Metrics.Select(m => m.Key));
        Assert.All(clean.Parts[0].Metrics, m => Assert.Equal("CPU fan", m.Label));
        var tracker = new MetricTracker();
        tracker.Record(clean, 0);
        Assert.Equal(900, tracker.Get(PartKind.Motherboard, "f:CPU Fan").MinMax.Max);
        Assert.Equal(2400, tracker.Get(PartKind.Motherboard, "f:CPU Fan#2").MinMax.Max);
        Assert.Equal(0, tracker.Get(PartKind.Motherboard, "f:CPU Fan#3").MinMax.Max);
    }

    [Fact]
    public void RenamedKeyDoesNotCollideWithARealKey()
    {
        var part = new PartSnapshot(PartKind.Cpu, "CPU", "", new[]
        {
            new Metric("a", "A", MetricUnit.Percent, Reading.Of(1)),
            new Metric("a#2", "A2", MetricUnit.Percent, Reading.Of(2)),
            new Metric("a", "A", MetricUnit.Percent, Reading.Of(3)),
        });
        Assert.Equal(new[] { "a", "a#2", "a#3" }, SnapshotGuard.Sanitize(Snap(part)).Parts[0].Metrics.Select(m => m.Key));
    }

    [Fact]
    public void DuplicatePartsKeepTheFirst()
    {
        SystemSnapshot clean = SnapshotGuard.Sanitize(Snap(Drive(30), Drive(99)));
        Assert.Single(clean.Parts);
        Assert.Equal(30, clean.Parts[0].Find(MetricKeys.Temperature)!.Reading.Value);
    }

    [Fact]
    public void CleanSnapshotIsReturnedUnchanged()
    {
        SystemSnapshot snap = new FakeSensorProvider().Read();
        Assert.Same(snap, SnapshotGuard.Sanitize(snap));
    }

    [Fact]
    public void EmptyHardwareListIsFine()
    {
        var tracker = new MetricTracker();
        SystemSnapshot empty = SnapshotGuard.Sanitize(Snap());
        tracker.Record(empty, 0);
        tracker.ResetMinMax();
        Assert.False(tracker.TryGet(PartKind.Cpu, MetricKeys.Load, out _));
    }

    [Fact]
    public void ResetMinMaxClearsEveryCard()
    {
        var tracker = new MetricTracker();
        var fake = new FakeSensorProvider();
        for (int i = 0; i < 20; i++)
            tracker.Record(fake.Read(), i);
        SystemSnapshot last = fake.Read();
        tracker.Record(last, 20);
        tracker.ResetMinMax();
        foreach (PartSnapshot p in last.Parts)
            foreach (Metric m in p.Metrics)
            {
                Assert.True(tracker.TryGet(p.Kind, m.Key, out MetricHistory? h));
                Assert.Null(h!.MinMax.Min);
                Assert.Null(h.MinMax.Max);
                // History (sparkline) is not part of min/max and stays.
                Assert.NotEmpty(h.History.Snapshot());
            }
        // The next sample starts min/max again from that sample alone.
        SystemSnapshot next = fake.Read();
        tracker.Record(next, 21);
        Metric load = next.Find(PartKind.Cpu)!.Find(MetricKeys.Load)!;
        Assert.Equal(load.Reading.Value, tracker.Get(PartKind.Cpu, MetricKeys.Load).MinMax.Min);
        Assert.Equal(load.Reading.Value, tracker.Get(PartKind.Cpu, MetricKeys.Load).MinMax.Max);
    }

    // ---------- °C / °F ----------

    [Theory]
    [InlineData(-40.0, 105.0)]
    [InlineData(0.0, 100.0)]
    [InlineData(36.6, 89.4)]
    public void UnitToggleShowsTheSameMinMaxConverted(double minC, double maxC)
    {
        var tracker = new MetricTracker();
        foreach (double c in new[] { maxC, minC, (minC + maxC) / 2 })
            tracker.Record(Snap(new PartSnapshot(PartKind.Cpu, "CPU", "", new[]
                { new Metric(MetricKeys.Temperature, "T", MetricUnit.Celsius, Reading.Of(c)) })), 0);
        MinMaxTracker mm = tracker.Get(PartKind.Cpu, MetricKeys.Temperature).MinMax;
        string inF = Formatter.FormatMinMax(MetricUnit.Celsius, mm.Min, mm.Max, TemperatureUnit.Fahrenheit);
        string inC = Formatter.FormatMinMax(MetricUnit.Celsius, mm.Min, mm.Max, TemperatureUnit.Celsius);
        Assert.Equal($"min {(minC * 9 / 5 + 32).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} · max {(maxC * 9 / 5 + 32).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} °F", inF);
        Assert.Equal($"min {minC.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} · max {maxC.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} °C", inC);
        // Toggling back and forth changes nothing stored: min/max and history stay in °C.
        Assert.Equal(minC, mm.Min);
        Assert.Equal(maxC, mm.Max);
    }

    // ---------- GPU choice ----------

    [Fact]
    public void DiscreteGpuWinsOverIntegrated()
    {
        var igpu = new GpuCandidate("AMD Radeon(TM) Graphics", GpuVendor.Amd, 512);
        var dgpu = new GpuCandidate("AMD Radeon RX 9060 XT", GpuVendor.Amd, 16304);
        Assert.Equal(1, GpuPicker.Pick(new[] { igpu, dgpu }));
        Assert.Equal(0, GpuPicker.Pick(new[] { dgpu, igpu }));
    }

    [Fact]
    public void DiscreteGpuWinsEvenWithoutAMemorySensor()
    {
        var igpu = new GpuCandidate("AMD Radeon(TM) Graphics", GpuVendor.Amd, 2048);
        var dgpu = new GpuCandidate("AMD Radeon RX 9060 XT", GpuVendor.Amd, null);
        Assert.Equal(1, GpuPicker.Pick(new[] { igpu, dgpu }));
    }

    [Fact]
    public void NvidiaDiscreteWinsOverAmdIntegrated()
    {
        var igpu = new GpuCandidate("AMD Radeon(TM) Graphics", GpuVendor.Amd, 512);
        var dgpu = new GpuCandidate("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia, 12282);
        Assert.Equal(1, GpuPicker.Pick(new[] { igpu, dgpu }));
    }

    [Fact]
    public void IntelArcDiscreteWinsOverIntelIntegrated()
    {
        var igpu = new GpuCandidate("Intel(R) UHD Graphics 770", GpuVendor.Intel, 128);
        var arc = new GpuCandidate("Intel(R) Arc(TM) A770 Graphics", GpuVendor.Intel, 16000);
        Assert.Equal(1, GpuPicker.Pick(new[] { igpu, arc }));
    }

    [Theory]
    [InlineData("AMD Radeon(TM) Graphics", GpuVendor.Amd, true)]
    [InlineData("AMD Radeon Graphics", GpuVendor.Amd, true)]
    [InlineData("AMD Radeon 780M Graphics", GpuVendor.Amd, true)]
    [InlineData("Radeon Vega 8 Graphics", GpuVendor.Amd, true)]
    [InlineData("AMD Radeon RX 9060 XT", GpuVendor.Amd, false)]
    [InlineData("AMD Radeon PRO W7600", GpuVendor.Amd, false)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, true)]
    [InlineData("Intel(R) Arc(TM) Graphics", GpuVendor.Intel, true)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", GpuVendor.Intel, false)]
    [InlineData("NVIDIA GeForce RTX 3060", GpuVendor.Nvidia, false)]
    public void IntegratedDetection(string name, GpuVendor vendor, bool integrated) =>
        Assert.Equal(integrated, GpuPicker.IsIntegrated(new GpuCandidate(name, vendor, null)));

    [Fact]
    public void SingleOrNoGpu()
    {
        Assert.Null(GpuPicker.Pick(Array.Empty<GpuCandidate>()));
        Assert.Equal(0, GpuPicker.Pick(new[] { new GpuCandidate("AMD Radeon(TM) Graphics", GpuVendor.Amd, double.NaN) }));
    }
}
