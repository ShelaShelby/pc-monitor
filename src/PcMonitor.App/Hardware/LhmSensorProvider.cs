using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using PcMonitor.Core;

namespace PcMonitor.App.Hardware;

/// <summary>Real sensors: LibreHardwareMonitorLib, with ADL as GPU fallback and WMI for specs.</summary>
internal sealed class LhmSensorProvider : ISensorProvider
{
    private const string NeedsPawnIo = "needs PawnIO driver";

    private Computer? _computer;
    private AdlFallback? _adl;
    private string _adlReason = "ADL not initialised";
    private DriverStatus _pawnIo = new(DriverState.Missing, "not checked yet");
    private readonly Dictionary<string, PhysicalDriveInfo?> _drives = new();
    private string? _networkId;
    private IReadOnlyList<PartSpecs>? _specs;
    private int _networkChecks;
    // WMI / registry results that do not change while running; read once instead of every update.
    private string? _ramSummary;
    private string? _boardName;
    private readonly Dictionary<string, double?> _gpuMemoryBytes = new(StringComparer.Ordinal);

    public string Name => "LibreHardwareMonitor";

    public DriverStatus PawnIo => _pawnIo;

    private bool PawnReady => _pawnIo.State == DriverState.Ready;

    public void Open()
    {
        _pawnIo = PawnIoDetector.Detect();
        Log.Write($"PawnIO: {_pawnIo.State} - {_pawnIo.Detail}");
        OpenComputer();
        foreach (string letter in new[] { "C:", "D:" })
        {
            try
            {
                _drives[letter] = DriveMapper.ForLetter(letter);
            }
            catch (Exception ex)
            {
                // A broken WMI must not stop the app; the drive card shows what DriveInfo still gives.
                Log.Write($"Drive mapping {letter} failed: {ex}");
                _drives[letter] = null;
            }
        }
    }

    private void OpenComputer()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
            IsNetworkEnabled = true,
        };
        computer.Open();
        _computer = computer;
        foreach (IHardware hw in computer.Hardware)
            Log.Write($"Hardware: {hw.HardwareType} '{hw.Name}' {hw.Identifier} sensors={hw.Sensors.Length} sub={hw.SubHardware.Length}");

        _adl?.Dispose();
        _adl = AdlFallback.TryCreate(PickGpu()?.Name, out _adlReason);
        Log.Write(_adl is null ? $"ADL fallback unavailable: {_adlReason}" : $"ADL fallback on '{_adl.AdapterName}'");
    }

    public void Recheck()
    {
        DriverStatus before = _pawnIo;
        _pawnIo = PawnIoDetector.Detect();
        Log.Write($"Recheck PawnIO: {_pawnIo.State} - {_pawnIo.Detail}");
        if (before.State != _pawnIo.State)
        {
            // LibreHardwareMonitor binds its drivers at Open(); reopen to pick up PawnIO.
            _computer?.Close();
            OpenComputer();
            _specs = null;
        }
    }

    public void Reopen()
    {
        Log.Write("Reopening LibreHardwareMonitor (resume from sleep)");
        _computer?.Close();
        _computer = null;
        _networkId = null;
        OpenComputer();
    }

    public void Dispose()
    {
        _adl?.Dispose();
        _adl = null;
        _computer?.Close();
        _computer = null;
    }

    // ---------- reading ----------

    public SystemSnapshot Read()
    {
        Computer computer = _computer ?? throw new InvalidOperationException("Open() first");
        foreach (IHardware hw in computer.Hardware)
            UpdateRecursive(hw);

        var parts = new List<PartSnapshot>
        {
            Safe(PartKind.Cpu, ReadCpu),
            Safe(PartKind.Gpu, ReadGpu),
            Safe(PartKind.Ram, ReadRam),
            Safe(PartKind.DriveC, () => ReadDrive(PartKind.DriveC, "C:")),
            Safe(PartKind.DriveD, () => ReadDrive(PartKind.DriveD, "D:")),
            Safe(PartKind.Motherboard, ReadBoard),
            Safe(PartKind.Network, ReadNetwork),
        };
        return new SystemSnapshot(DateTimeOffset.Now, parts);
    }

    private static void UpdateRecursive(IHardware hw)
    {
        try
        {
            hw.Update();
        }
        catch (Exception ex)
        {
            Log.Write($"Update '{hw.Name}' failed: {ex.Message}");
        }
        foreach (IHardware sub in hw.SubHardware)
            UpdateRecursive(sub);
    }

    private static PartSnapshot Safe(PartKind kind, Func<PartSnapshot> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Write($"Reading {kind} failed: {ex}");
            return new PartSnapshot(kind, CardLayout.TitleOf(kind), "", Array.Empty<Metric>(), $"Error reading sensors: {ex.Message}");
        }
    }

    private IEnumerable<IHardware> All(params HardwareType[] types)
    {
        if (_computer is null)
            yield break;
        foreach (IHardware hw in _computer.Hardware)
        {
            if (types.Contains(hw.HardwareType))
                yield return hw;
            foreach (IHardware sub in hw.SubHardware)
                if (types.Contains(sub.HardwareType))
                    yield return sub;
        }
    }

    private static ISensor? Find(IHardware? hw, SensorType type, params string[] names)
    {
        if (hw is null)
            return null;
        foreach (string name in names)
        {
            ISensor? s = hw.Sensors.FirstOrDefault(x => x.SensorType == type && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (s is not null)
                return s;
        }
        return null;
    }

    private static double? Val(ISensor? s) => s?.Value is float f && !float.IsNaN(f) && !float.IsInfinity(f) ? f : null;

    // ---------- CPU ----------

    private PartSnapshot ReadCpu()
    {
        IHardware? cpu = All(HardwareType.Cpu).FirstOrDefault();
        if (cpu is null)
            return Missing(PartKind.Cpu, "LibreHardwareMonitor found no CPU");

        string tempReason = PawnReady ? "not exposed by sensor" : NeedsPawnIo;
        ISensor? temp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)", "CPU Package", "Package")
                        ?? cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
        // Right after start LibreHardwareMonitor reports 0 for Ryzen temperature, clocks and power
        // until the first real sample; SensorGuards turns those into "waiting for first sample"
        // (or "needs PawnIO driver" while the driver is missing, since then they never update).
        string zero = PawnReady ? SensorGuards.WaitingForFirstSample : NeedsPawnIo;
        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(Val(Find(cpu, SensorType.Load, "CPU Total")), "not exposed by sensor")),
            new(MetricKeys.Temperature, "Tctl/Tdie", MetricUnit.Celsius, SensorGuards.CpuValue(Val(temp), tempReason, zero), TemperatureZone.CpuCore),
        };
        foreach (ISensor ccd in cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase)
                                                        && !s.Name.Contains("Max", StringComparison.OrdinalIgnoreCase)
                                                        && !s.Name.Contains("Average", StringComparison.OrdinalIgnoreCase)))
            metrics.Add(new Metric("ccd:" + ccd.Name, ccd.Name.Replace(" (Tdie)", ""), MetricUnit.Celsius, SensorGuards.CpuValue(Val(ccd), tempReason, zero), TemperatureZone.CpuCore));
        if (!metrics.Any(m => m.Key.StartsWith("ccd:", StringComparison.Ordinal)))
            metrics.Add(new Metric("ccd:CCD1", "CCD1", MetricUnit.Celsius, Reading.Unavailable(tempReason), TemperatureZone.CpuCore));

        NamedValue[] clocks = cpu.Sensors.Where(s => s.SensorType == SensorType.Clock).Select(s => new NamedValue(s.Name, Val(s))).ToArray();
        ClockResult avgClock = CpuClocks.Average(clocks);
        ClockResult maxClock = CpuClocks.MaxCore(clocks);
        LogClocksOnce(clocks, avgClock, maxClock);
        string clockReason = PawnReady ? "not exposed by sensor" : NeedsPawnIo;
        metrics.Add(new Metric(MetricKeys.CoreClock, "Clock (avg)", MetricUnit.MegaHertz, avgClock.ToReading(clockReason, zero)));
        metrics.Add(new Metric(MetricKeys.MaxCoreClock, "Clock (max core)", MetricUnit.MegaHertz, maxClock.ToReading(clockReason, zero)));
        metrics.Add(new Metric(MetricKeys.Power, "Package power", MetricUnit.Watt,
            SensorGuards.CpuValue(Val(Find(cpu, SensorType.Power, "Package", "CPU Package")), PawnReady ? "not exposed by sensor" : NeedsPawnIo, zero)));

        string? notice = PawnReady ? null : "Temperatures, clocks and power need the PawnIO driver.";
        return new PartSnapshot(PartKind.Cpu, "CPU", cpu.Name, metrics, notice);
    }

    private int _clockLogReads;

    /// <summary>
    /// Writes every CPU clock sensor to the log once, at the first read with a non-zero clock
    /// (or after 10 reads), so a wrong-looking clock can be diagnosed from pc-monitor.log.
    /// </summary>
    private void LogClocksOnce(NamedValue[] clocks, ClockResult avg, ClockResult max)
    {
        if (_clockLogReads < 0)
            return;
        _clockLogReads++;
        if (_clockLogReads < 10 && !clocks.Any(c => c.Value > 0))
            return;
        Log.Write($"CPU clock sensors ({clocks.Length}) at read {_clockLogReads}:");
        _clockLogReads = -1;
        foreach (NamedValue c in clocks)
            Log.Write($"  Clock '{c.Name}' = {(c.Value.HasValue ? c.Value.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "null")} MHz");
        Log.Write($"  avg from {avg.Source}: {avg.Value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}; "
                  + $"max from {max.Source}: {max.Value?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"}");
    }

    // ---------- GPU ----------

    private IHardware? PickGpu()
    {
        // Discrete before integrated (the RX 9060 XT over the Ryzen iGPU), then the most dedicated memory.
        IHardware[] gpus = All(HardwareType.GpuAmd, HardwareType.GpuNvidia, HardwareType.GpuIntel).ToArray();
        GpuCandidate[] candidates = gpus.Select(g => new GpuCandidate(
            g.Name,
            g.HardwareType switch { HardwareType.GpuNvidia => GpuVendor.Nvidia, HardwareType.GpuIntel => GpuVendor.Intel, _ => GpuVendor.Amd },
            Val(Find(g, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total")))).ToArray();
        return GpuPicker.Pick(candidates) is int i ? gpus[i] : null;
    }

    private double? GpuMemoryBytes(string? name)
    {
        string key = name ?? "";
        if (!_gpuMemoryBytes.TryGetValue(key, out double? bytes))
            _gpuMemoryBytes[key] = bytes = SystemSpecs.GpuMemoryBytes(name);
        return bytes;
    }

    private PartSnapshot ReadGpu()
    {
        IHardware? gpu = PickGpu();
        IReadOnlyDictionary<int, int> adl = _adl?.Query() ?? new Dictionary<int, int>();
        string missing = _adl is null ? $"not exposed (LHM; {_adlReason})" : "not exposed by driver (LHM + ADL)";

        double? FromAdl(params int[] ids)
        {
            foreach (int id in ids)
                if (adl.TryGetValue(id, out int v))
                    return v;
            return null;
        }

        if (gpu is null && adl.Count == 0)
            return Missing(PartKind.Gpu, "no GPU found by LibreHardwareMonitor or ADL");

        double? vramUsedMb = Val(Find(gpu, SensorType.SmallData, "GPU Memory Used")) ?? Val(Find(gpu, SensorType.SmallData, "D3D Dedicated Memory Used"));
        double? vramTotalMb = Val(Find(gpu, SensorType.SmallData, "GPU Memory Total")) ?? Val(Find(gpu, SensorType.SmallData, "D3D Dedicated Memory Total"));
        double? vramTotalBytes = vramTotalMb * UnitConversion.BytesPerMiB ?? GpuMemoryBytes(gpu?.Name);

        Reading edge = Reading.Of(Val(Find(gpu, SensorType.Temperature, "GPU Core")) ?? FromAdl(AdlFallback.TempEdge, AdlFallback.TempGfx), missing);
        // 0 MHz / 0 W together with no temperature is the driver not having sampled yet, not an idle GPU.
        bool tempMissing = !edge.HasValue;
        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(Val(Find(gpu, SensorType.Load, "GPU Core")) ?? FromAdl(AdlFallback.ActivityGfx), missing)),
            new(MetricKeys.Temperature, "Edge", MetricUnit.Celsius, edge, TemperatureZone.GpuEdge),
            new(MetricKeys.Hotspot, "Hotspot", MetricUnit.Celsius,
                Reading.Of(Val(Find(gpu, SensorType.Temperature, "GPU Hot Spot")) ?? FromAdl(AdlFallback.TempHotspot), missing), TemperatureZone.GpuHotspot),
            new(MetricKeys.MemoryTemperature, "Memory temp", MetricUnit.Celsius,
                Reading.Of(Val(Find(gpu, SensorType.Temperature, "GPU Memory")) ?? FromAdl(AdlFallback.TempMem), missing), TemperatureZone.GpuMemory),
            new(MetricKeys.VramUsed, "VRAM used", MetricUnit.Bytes, Reading.Of(vramUsedMb * UnitConversion.BytesPerMiB, missing)),
            new(MetricKeys.VramTotal, "VRAM total", MetricUnit.Bytes, Reading.Of(vramTotalBytes, missing)),
            new(MetricKeys.CoreClock, "Core clock", MetricUnit.MegaHertz, SensorGuards.GpuClockOrPower(Val(Find(gpu, SensorType.Clock, "GPU Core")) ?? FromAdl(AdlFallback.ClkGfx), tempMissing, missing)),
            new(MetricKeys.MemoryClock, "Memory clock", MetricUnit.MegaHertz, SensorGuards.GpuClockOrPower(Val(Find(gpu, SensorType.Clock, "GPU Memory")) ?? FromAdl(AdlFallback.ClkMem), tempMissing, missing)),
            new(MetricKeys.Power, "Power", MetricUnit.Watt,
                SensorGuards.GpuClockOrPower(Val(Find(gpu, SensorType.Power, "GPU Package", "GPU PPT", "GPU Core")) ?? FromAdl(AdlFallback.BoardPower, AdlFallback.AsicPower), tempMissing, missing)),
            new(MetricKeys.Fan, "Fan", MetricUnit.Rpm, Reading.Of(Val(Find(gpu, SensorType.Fan, "GPU Fan")) ?? FromAdl(AdlFallback.FanRpm), missing)),
            new(MetricKeys.FanPercent, "Fan duty", MetricUnit.Percent, Reading.Of(Val(Find(gpu, SensorType.Control, "GPU Fan")) ?? FromAdl(AdlFallback.FanPercent), missing)),
        };
        return new PartSnapshot(PartKind.Gpu, "GPU", gpu?.Name ?? _adl?.AdapterName ?? "GPU", metrics);
    }

    // ---------- RAM ----------

    private PartSnapshot ReadRam()
    {
        IHardware? mem = All(HardwareType.Memory)
            .FirstOrDefault(h => !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && Find(h, SensorType.Load, "Memory") is not null);
        double? usedGb = Val(Find(mem, SensorType.Data, "Memory Used"));
        double? availGb = Val(Find(mem, SensorType.Data, "Memory Available"));

        ISensor[] dimms = All(HardwareType.Memory)
            .SelectMany(h => h.Sensors)
            .Where(s => s.SensorType == SensorType.Temperature && s.Name.StartsWith("DIMM", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string dimmReason = PawnReady ? "no DIMM temperature sensor exposed" : NeedsPawnIo + " (SMBus)";
        double[] dimmTemps = dimms.Select(Val).Where(v => v.HasValue).Select(v => v!.Value).ToArray();

        var metrics = new List<Metric>
        {
            new(MetricKeys.Load, "Load", MetricUnit.Percent, Reading.Of(Val(Find(mem, SensorType.Load, "Memory")), "not exposed")),
            new(MetricKeys.Temperature, "DIMM max", MetricUnit.Celsius, Reading.Of(dimmTemps.Length > 0 ? dimmTemps.Max() : null, dimmReason), TemperatureZone.Dimm),
        };
        foreach (ISensor d in dimms)
            metrics.Add(new Metric("dimm:" + d.Name, d.Name, MetricUnit.Celsius, Reading.Of(Val(d), dimmReason), TemperatureZone.Dimm));
        metrics.Add(new Metric(MetricKeys.Used, "Used", MetricUnit.Bytes, Reading.Of(usedGb * UnitConversion.BytesPerGiB, "not exposed")));
        metrics.Add(new Metric(MetricKeys.Available, "Available", MetricUnit.Bytes, Reading.Of(availGb * UnitConversion.BytesPerGiB, "not exposed")));
        metrics.Add(new Metric(MetricKeys.Total, "Total", MetricUnit.Bytes, Reading.Of((usedGb + availGb) * UnitConversion.BytesPerGiB, "not exposed")));
        if (string.IsNullOrEmpty(_ramSummary))
            _ramSummary = SystemSpecs.RamSummary();
        return new PartSnapshot(PartKind.Ram, "RAM", _ramSummary, metrics);
    }

    // ---------- drives ----------

    private IHardware? StorageFor(PhysicalDriveInfo info)
    {
        IHardware[] storages = All(HardwareType.Storage).ToArray();
        IHardware[] byName = storages.Where(s => Normalize(s.Name) == Normalize(info.Model)).ToArray();
        if (byName.Length == 1)
            return byName[0];
        IHardware[] candidates = byName.Length > 1 ? byName : storages.Where(s => Normalize(info.Model).Contains(Normalize(s.Name)) || Normalize(s.Name).Contains(Normalize(info.Model))).ToArray();
        if (candidates.Length == 1)
            return candidates[0];
        // Same model twice: LibreHardwareMonitor identifiers end with the physical drive index.
        return candidates.FirstOrDefault(s => s.Identifier.ToString().EndsWith("/" + info.DiskIndex, StringComparison.Ordinal));
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private PartSnapshot ReadDrive(PartKind kind, string letter)
    {
        _drives.TryGetValue(letter, out PhysicalDriveInfo? info);
        DriveInfo? volume = null;
        try
        {
            var d = new DriveInfo(letter);
            if (d.IsReady)
                volume = d;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"DriveInfo {letter}: {ex.Message}");
        }
        if (volume is null && info is null)
            return Missing(kind, $"no drive {letter} found");

        IHardware? storage = info is null ? null : StorageFor(info);
        string smartReason = storage is null ? "drive not found by LibreHardwareMonitor" : "not reported by drive (SMART)";
        ISensor? temp = Find(storage, SensorType.Temperature, "Composite Temperature", "Temperature")
                        ?? storage?.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature
                                                                && !s.Name.Contains("Warning", StringComparison.OrdinalIgnoreCase)
                                                                && !s.Name.Contains("Critical", StringComparison.OrdinalIgnoreCase));
        TemperatureThresholds thresholds = ThresholdTable.ForNvme(
            Val(Find(storage, SensorType.Temperature, "Warning Temperature")),
            Val(Find(storage, SensorType.Temperature, "Critical Temperature")));
        double? health = Val(Find(storage, SensorType.Level, "Life", "Remaining Life"));
        if (!health.HasValue && Val(Find(storage, SensorType.Level, "Percentage Used")) is double used)
            health = Math.Max(0, 100 - used);

        var metrics = new List<Metric>
        {
            new(MetricKeys.Activity, "Activity", MetricUnit.Percent, Reading.Of(Val(Find(storage, SensorType.Load, "Total Activity")), smartReason)),
            new(MetricKeys.Temperature, "Temperature", MetricUnit.Celsius, Reading.Of(Val(temp), smartReason), TemperatureZone.Nvme, thresholds),
            new(MetricKeys.Health, "Health", MetricUnit.Percent, Reading.Of(health, smartReason)),
            new(MetricKeys.PowerOnHours, "Power-on hours", MetricUnit.Hours, Reading.Of(Val(Find(storage, SensorType.Factor, "Power On Hours")), smartReason)),
            new(MetricKeys.Free, "Free", MetricUnit.Bytes, Reading.Of(volume is null ? null : (double)volume.AvailableFreeSpace, "volume not ready")),
            new(MetricKeys.Total, "Total", MetricUnit.Bytes, Reading.Of(volume is null ? null : (double)volume.TotalSize, "volume not ready")),
            new(MetricKeys.ReadRate, "Read", MetricUnit.BytesPerSecond, Reading.Of(Val(Find(storage, SensorType.Throughput, "Read Rate")), smartReason)),
            new(MetricKeys.WriteRate, "Write", MetricUnit.BytesPerSecond, Reading.Of(Val(Find(storage, SensorType.Throughput, "Write Rate")), smartReason)),
        };
        string model = info?.Model ?? storage?.Name ?? "unknown drive";
        return new PartSnapshot(kind, CardLayout.TitleOf(kind), $"{letter} {model}", metrics);
    }

    // ---------- motherboard / fans ----------

    private PartSnapshot ReadBoard()
    {
        IHardware? board = All(HardwareType.Motherboard).FirstOrDefault();
        IHardware[] chips = All(HardwareType.SuperIO, HardwareType.EmbeddedController, HardwareType.Cooler).ToArray();
        var metrics = new List<Metric>();
        var temps = new List<double>();
        var fans = new List<double>();

        foreach (IHardware chip in chips)
        {
            string prefix = chip.HardwareType == HardwareType.Cooler ? chip.Name + " " : "";
            foreach (ISensor s in chip.Sensors.Where(s => s.SensorType == SensorType.Temperature))
            {
                double? v = Val(s);
                // SuperIO inputs with nothing attached read -128/127/255 or similar; those are not temperatures.
                Reading r = v is > -40 and < 125 ? Reading.Of(v.Value) : Reading.Unavailable(v.HasValue ? "sensor not connected" : "no value");
                if (r.HasValue)
                    temps.Add(r.Value!.Value);
                metrics.Add(new Metric($"t:{chip.Identifier}/{s.Name}", prefix + s.Name, MetricUnit.Celsius, r, TemperatureZone.Motherboard));
            }
            foreach (ISensor s in chip.Sensors.Where(s => s.SensorType == SensorType.Fan))
            {
                double? v = Val(s);
                Reading r = v is >= 0 and < 20000 ? Reading.Of(v.Value) : Reading.Unavailable("no value");
                if (r.HasValue)
                    fans.Add(r.Value!.Value);
                metrics.Add(new Metric($"f:{chip.Identifier}/{s.Name}", prefix + s.Name, MetricUnit.Rpm, r));
            }
        }

        string reason = chips.Length == 0
            ? (PawnReady ? "board sensor chip not supported" : NeedsPawnIo + " (SuperIO)")
            : "no sensor reported";
        metrics.Insert(0, new Metric(MetricKeys.FanMax, "Fastest fan", MetricUnit.Rpm, Reading.Of(fans.Count > 0 ? fans.Max() : null, reason)));
        metrics.Insert(0, new Metric(MetricKeys.TemperatureMax, "Hottest sensor", MetricUnit.Celsius,
            Reading.Of(temps.Count > 0 ? temps.Max() : null, reason), TemperatureZone.Motherboard));
        string? notice = chips.Length == 0 ? $"No fan/temperature chip readable: {reason}." : null;
        return new PartSnapshot(PartKind.Motherboard, CardLayout.TitleOf(PartKind.Motherboard), board?.Name ?? (_boardName ??= SystemSpecs.BoardName()), metrics, notice);
    }

    // ---------- network ----------

    private PartSnapshot ReadNetwork()
    {
        IHardware[] nics = All(HardwareType.Network).ToArray();
        IHardware? nic = nics.FirstOrDefault(n => n.Identifier.ToString() == _networkId);
        // Every 20 reads: drop the adapter if Windows says it is down (cable unplugged, VPN off) and another is up.
        if (nic is not null && ++_networkChecks % 20 == 0 && !IsUp(nic.Name) && nics.Any(n => n != nic && IsUp(n.Name)))
        {
            Log.Write($"Network adapter '{nic.Name}' is down; choosing another");
            nics = nics.Where(n => n != nic && IsUp(n.Name)).ToArray();
            nic = null;
        }
        if (nic is null)
        {
            // Busiest adapter by total traffic so far; stays selected while it exists.
            nic = nics.OrderByDescending(n => (Val(Find(n, SensorType.Data, "Data Downloaded")) ?? 0) + (Val(Find(n, SensorType.Data, "Data Uploaded")) ?? 0))
                .FirstOrDefault();
            _networkId = nic?.Identifier.ToString();
        }
        if (nic is null)
            return Missing(PartKind.Network, "no active network adapter");

        var metrics = new List<Metric>
        {
            new(MetricKeys.Download, "Download", MetricUnit.BytesPerSecond, Reading.Of(Val(Find(nic, SensorType.Throughput, "Download Speed")), "not exposed")),
            new(MetricKeys.Upload, "Upload", MetricUnit.BytesPerSecond, Reading.Of(Val(Find(nic, SensorType.Throughput, "Upload Speed")), "not exposed")),
            new(MetricKeys.Load, "Utilization", MetricUnit.Percent, Reading.Of(Val(Find(nic, SensorType.Load, "Network Utilization")), "not exposed")),
            new("total_down", "Downloaded", MetricUnit.Bytes, Reading.Of(Val(Find(nic, SensorType.Data, "Data Downloaded")) * UnitConversion.BytesPerGiB, "not exposed")),
            new("total_up", "Uploaded", MetricUnit.Bytes, Reading.Of(Val(Find(nic, SensorType.Data, "Data Uploaded")) * UnitConversion.BytesPerGiB, "not exposed")),
        };
        return new PartSnapshot(PartKind.Network, "Network", nic.Name, metrics);
    }

    private static PartSnapshot Missing(PartKind kind, string reason)
    {
        CardLayout layout = CardLayout.For(kind);
        var metrics = new List<Metric> { new(layout.HeroKey, "Value", MetricUnit.Percent, Reading.Unavailable(reason)) };
        return new PartSnapshot(kind, CardLayout.TitleOf(kind), "", metrics, reason);
    }

    // ---------- specs ----------

    public IReadOnlyList<PartSpecs> GetSpecs()
    {
        if (_specs is not null)
            return _specs;
        IHardware? nic = All(HardwareType.Network).FirstOrDefault(n => n.Identifier.ToString() == _networkId);
        _specs = SystemSpecs.Collect(
            gpuName: PickGpu()?.Name,
            driveC: _drives.GetValueOrDefault("C:"),
            driveD: _drives.GetValueOrDefault("D:"),
            networkName: nic?.Name,
            boardFromLhm: All(HardwareType.Motherboard).FirstOrDefault()?.Name,
            superIo: All(HardwareType.SuperIO).Select(h => h.Name).ToArray());
        return _specs;
    }

    private static bool IsUp(string name)
    {
        try
        {
            return FindInterface(name)?.OperationalStatus is not (OperationalStatus.Down or OperationalStatus.NotPresent or OperationalStatus.LowerLayerDown);
        }
        catch (NetworkInformationException)
        {
            return true;
        }
    }

    internal static NetworkInterface? FindInterface(string? name) =>
        name is null ? null : NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name == name || n.Description == name);
}
