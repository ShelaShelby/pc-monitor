namespace PcMonitor.Core;

/// <summary>
/// Simulated hardware shaped like the target PC. Deterministic for a given seed and tick count,
/// so screenshots and tests are reproducible. A few values are deliberately "n/a" to exercise
/// the unavailable path in the UI.
/// </summary>
public sealed class FakeSensorProvider : ISensorProvider
{
    private readonly Random _random;
    private readonly bool _pawnIoInstalled;
    private readonly DateTimeOffset _start;
    private int _tick;

    public FakeSensorProvider(int seed = 7500, bool pawnIoInstalled = true, DateTimeOffset? start = null)
    {
        _random = new Random(seed);
        _pawnIoInstalled = pawnIoInstalled;
        _start = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public string Name => "Simulated hardware (FakeSensorProvider)";

    public DriverStatus PawnIo => _pawnIoInstalled
        ? new DriverStatus(DriverState.NotApplicable, "simulated")
        : new DriverStatus(DriverState.Missing, "Simulated: PawnIO driver is not installed.");

    public void Open() { }

    public void Recheck() { }

    public void Dispose() { }

    private double Wave(double period, double phase = 0) => Math.Sin((_tick + phase) * 2 * Math.PI / period);

    private double Noise(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

    public SystemSnapshot Read()
    {
        _tick++;
        var parts = new List<PartSnapshot>
        {
            Cpu(),
            Gpu(),
            Ram(),
            Drive(PartKind.DriveC, "Samsung SSD 990 PRO 2TB", 2000398934016, 1_200_000_000_000, 41, 98, 3120),
            Drive(PartKind.DriveD, "WD_BLACK SN850X 2000GB", 2000398934016, 640_000_000_000, 37, 100, 1890),
            Board(),
            Network(),
        };
        return new SystemSnapshot(_start.AddSeconds(_tick), parts);
    }

    private PartSnapshot Cpu()
    {
        double load = Clamp(38 + 30 * Wave(23) + Noise(8), 1, 100);
        double temp = 52 + 0.32 * load + Noise(1.5);
        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(load)),
            _pawnIoInstalled
                ? new(MetricKeys.Temperature, "Tctl/Tdie", MetricUnit.Celsius, Reading.Of(temp), TemperatureZone.CpuCore)
                : new(MetricKeys.Temperature, "Tctl/Tdie", MetricUnit.Celsius, Reading.Unavailable("needs PawnIO driver"), TemperatureZone.CpuCore),
            _pawnIoInstalled
                ? new("ccd1", "CCD1", MetricUnit.Celsius, Reading.Of(temp - 3 + Noise(1)), TemperatureZone.CpuCore)
                : new("ccd1", "CCD1", MetricUnit.Celsius, Reading.Unavailable("needs PawnIO driver"), TemperatureZone.CpuCore),
            new(MetricKeys.CoreClock, "Clock (avg)", MetricUnit.MegaHertz, Reading.Of(4100 + 350 * Wave(11) + Noise(40))),
            new(MetricKeys.MaxCoreClock, "Clock (max core)", MetricUnit.MegaHertz, Reading.Of(4450 + Noise(30))),
            new(MetricKeys.Power, "Package power", MetricUnit.Watt, Reading.Of(28 + 0.5 * load + Noise(2))),
        };
        return new PartSnapshot(PartKind.Cpu, "CPU", "AMD Ryzen 5 7500X3D", metrics);
    }

    private PartSnapshot Gpu()
    {
        double load = Clamp(55 + 40 * Wave(31, 5) + Noise(6), 0, 100);
        double edge = 44 + 0.25 * load + Noise(1);
        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(load)),
            new(MetricKeys.Temperature, "Edge", MetricUnit.Celsius, Reading.Of(edge), TemperatureZone.GpuEdge),
            new(MetricKeys.Hotspot, "Hotspot", MetricUnit.Celsius, Reading.Of(edge + 18 + Noise(2)), TemperatureZone.GpuHotspot),
            new(MetricKeys.MemoryTemperature, "Memory temp", MetricUnit.Celsius, Reading.Unavailable("not exposed by driver"), TemperatureZone.GpuMemory),
            new(MetricKeys.VramUsed, "VRAM used", MetricUnit.Bytes, Reading.Of((5.2 + 1.5 * Wave(40)) * UnitConversion.BytesPerGiB)),
            new(MetricKeys.VramTotal, "VRAM total", MetricUnit.Bytes, Reading.Of(16 * UnitConversion.BytesPerGiB)),
            new(MetricKeys.CoreClock, "Core clock", MetricUnit.MegaHertz, Reading.Of(2600 + 6 * load + Noise(20))),
            new(MetricKeys.MemoryClock, "Memory clock", MetricUnit.MegaHertz, Reading.Of(2518)),
            new(MetricKeys.Power, "Power", MetricUnit.Watt, Reading.Of(18 + 1.6 * load + Noise(3))),
            new(MetricKeys.Fan, "Fan", MetricUnit.Rpm, Reading.Of(load > 30 ? 900 + 12 * load : 0)),
        };
        return new PartSnapshot(PartKind.Gpu, "GPU", "AMD Radeon RX 9060 XT", metrics);
    }

    private PartSnapshot Ram()
    {
        double total = 32 * UnitConversion.BytesPerGiB;
        double used = (14.5 + 2 * Wave(50) + Noise(0.2)) * UnitConversion.BytesPerGiB;
        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(100 * used / total)),
            new(MetricKeys.Temperature, "DIMM max", MetricUnit.Celsius, Reading.Of(41 + Noise(0.5)), TemperatureZone.Dimm),
            new(MetricKeys.Used, "Used", MetricUnit.Bytes, Reading.Of(used)),
            new(MetricKeys.Available, "Available", MetricUnit.Bytes, Reading.Of(total - used)),
            new(MetricKeys.Total, "Total", MetricUnit.Bytes, Reading.Of(total)),
        };
        return new PartSnapshot(PartKind.Ram, "RAM", "32 GB DDR5", metrics);
    }

    private PartSnapshot Drive(PartKind kind, string model, double total, double used, double baseTemp, double health, double hours)
    {
        double activity = Clamp(6 + 10 * Math.Max(0, Wave(17, (int)kind * 3)) + Noise(3), 0, 100);
        var metrics = new List<Metric>
        {
            new(MetricKeys.Activity, "Activity", MetricUnit.Percent, Reading.Of(activity)),
            new(MetricKeys.Temperature, "Temperature", MetricUnit.Celsius, Reading.Of(baseTemp + 0.1 * activity + Noise(0.4)), TemperatureZone.Nvme,
                ThresholdTable.ForNvme(82, 85)),
            new(MetricKeys.Health, "Health", MetricUnit.Percent, Reading.Of(health)),
            new(MetricKeys.PowerOnHours, "Power-on hours", MetricUnit.Hours, Reading.Of(hours + _tick / 3600.0)),
            new(MetricKeys.Free, "Free", MetricUnit.Bytes, Reading.Of(total - used)),
            new(MetricKeys.Total, "Total", MetricUnit.Bytes, Reading.Of(total)),
            new(MetricKeys.ReadRate, "Read", MetricUnit.BytesPerSecond, Reading.Of(activity * 4.2e6)),
            new(MetricKeys.WriteRate, "Write", MetricUnit.BytesPerSecond, Reading.Of(activity * 1.1e6)),
        };
        string letter = kind == PartKind.DriveC ? "C:" : "D:";
        return new PartSnapshot(kind, CardLayout.TitleOf(kind), $"{letter} {model}", metrics);
    }

    private PartSnapshot Board()
    {
        double sys = 34 + 2 * Wave(60) + Noise(0.3);
        double vrm = 46 + 4 * Wave(23) + Noise(0.5);
        double cpuFan = 950 + 250 * Wave(23) + Noise(10);
        var metrics = new List<Metric>
        {
            new(MetricKeys.TemperatureMax, "Hottest sensor", MetricUnit.Celsius, Reading.Of(Math.Max(sys, vrm)), TemperatureZone.Motherboard),
            new(MetricKeys.FanMax, "Fastest fan", MetricUnit.Rpm, Reading.Of(cpuFan)),
            new("t:System", "System", MetricUnit.Celsius, Reading.Of(sys), TemperatureZone.Motherboard),
            new("t:VRM MOS", "VRM MOS", MetricUnit.Celsius, Reading.Of(vrm), TemperatureZone.Motherboard),
            new("f:CPU Fan", "CPU fan", MetricUnit.Rpm, Reading.Of(cpuFan)),
            new("f:System Fan #1", "System fan 1", MetricUnit.Rpm, Reading.Of(780 + Noise(8))),
            new("f:System Fan #2", "System fan 2", MetricUnit.Rpm, Reading.Unavailable("no fan connected")),
        };
        return new PartSnapshot(PartKind.Motherboard, CardLayout.TitleOf(PartKind.Motherboard), "Simulated B650 board", metrics);
    }

    private PartSnapshot Network()
    {
        double down = Math.Max(0, 2.5e6 + 2.4e6 * Wave(19) + Noise(3e5));
        double up = Math.Max(0, 1.5e5 + 1.2e5 * Wave(13, 4) + Noise(2e4));
        var metrics = new List<Metric>
        {
            new(MetricKeys.Download, "Download", MetricUnit.BytesPerSecond, Reading.Of(down)),
            new(MetricKeys.Upload, "Upload", MetricUnit.BytesPerSecond, Reading.Of(up)),
            new(MetricKeys.Load, "Utilization", MetricUnit.Percent, Reading.Of(100 * (down + up) / 125e6)),
        };
        return new PartSnapshot(PartKind.Network, "Network", "Ethernet (simulated)", metrics);
    }

    public IReadOnlyList<PartSpecs> GetSpecs() => new[]
    {
        new PartSpecs(PartKind.Cpu, new[]
        {
            new SpecLine("Model", "AMD Ryzen 5 7500X3D 6-Core Processor"),
            new SpecLine("Cores / threads", "6 / 12"),
            new SpecLine("Base clock", "4,000 MHz"),
            new SpecLine("Boost clock", "n/a (not reported by Windows)"),
        }),
        new PartSpecs(PartKind.Gpu, new[]
        {
            new SpecLine("Model", "AMD Radeon RX 9060 XT"),
            new SpecLine("Memory", "16 GB"),
            new SpecLine("Driver", "32.0.0.0 (simulated)"),
        }),
        new PartSpecs(PartKind.Ram, new[]
        {
            new SpecLine("Installed", "32 GB"),
            new SpecLine("Modules", "2 × 16 GB DDR5"),
            new SpecLine("Speed", "6000 MT/s (configured)"),
            new SpecLine("Slots", "2 of 4 used"),
        }.Concat(RamLabels.For(new[] { new MemoryStick("DIMM 1", "P0 CHANNEL A"), new MemoryStick("DIMM 1", "P0 CHANNEL B") })
            .Select(label => new SpecLine(label, "16 GB DDR5 (simulated)"))).ToArray()),
        new PartSpecs(PartKind.DriveC, new[] { new SpecLine("Model", "Samsung SSD 990 PRO 2TB"), new SpecLine("Bus", "NVMe") }),
        new PartSpecs(PartKind.DriveD, new[] { new SpecLine("Model", "WD_BLACK SN850X 2000GB"), new SpecLine("Bus", "NVMe") }),
        new PartSpecs(PartKind.Motherboard, new[] { new SpecLine("Board", "Simulated B650"), new SpecLine("BIOS", "1.0") }),
        new PartSpecs(PartKind.Network, new[] { new SpecLine("Adapter", "Ethernet (simulated)"), new SpecLine("Link speed", "1 Gbit/s") }),
    };
}
