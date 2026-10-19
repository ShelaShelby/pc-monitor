using System.Globalization;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using PcMonitor.Core;

namespace PcMonitor.App.Hardware;

/// <summary>Static specs read from Windows (WMI, registry) where LibreHardwareMonitor has no data.</summary>
internal static class SystemSpecs
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Mhz(ulong? v) => v is > 0 ? $"{v.Value.ToString("#,0", Inv)} MHz" : "n/a (not reported by Windows)";

    private static string Size(ulong? bytes) => bytes is > 0 ? Formatter.FormatBytes(bytes.Value).ToString() : "n/a (not reported)";

    // SMBIOS memory type codes (DMTF SMBIOS spec, type 17).
    private static string MemoryType(ulong? code) => code switch
    {
        26 => "DDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        30 => "LPDDR4",
        24 => "DDR3",
        _ => "",
    };

    public static string RamSummary()
    {
        List<Dictionary<string, object?>> sticks = Wmi.Query("SELECT Capacity, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        ulong total = (ulong)sticks.Sum(s => (decimal)(s.ULong("Capacity") ?? 0));
        string type = sticks.Select(s => MemoryType(s.ULong("SMBIOSMemoryType"))).FirstOrDefault(t => t.Length > 0) ?? "";
        return total > 0 ? $"{Formatter.FormatBytes(total)} {type}".Trim() : "";
    }

    public static string BoardName()
    {
        Dictionary<string, object?>? b = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        return b is null ? "" : $"{b.Str("Manufacturer")} {b.Str("Product")}".Trim();
    }

    /// <summary>
    /// Dedicated VRAM from the display driver's registry entry. WMI's Win32_VideoController.AdapterRAM
    /// is a 32-bit field that caps at 4 GB, so it cannot be used for a 16 GB card.
    /// </summary>
    public static double? GpuMemoryBytes(string? gpuName)
    {
        const string ClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using RegistryKey? cls = Registry.LocalMachine.OpenSubKey(ClassKey);
            if (cls is null)
                return null;
            double? best = null;
            foreach (string sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                using RegistryKey? k = cls.OpenSubKey(sub);
                if (k is null)
                    continue;
                string? desc = k.GetValue("DriverDesc") as string;
                object? raw = k.GetValue("HardwareInformation.qwMemorySize");
                double? bytes = raw switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => null,
                };
                if (bytes is not > 0)
                    continue;
                if (gpuName is not null && desc is not null && desc.Trim().Equals(gpuName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return bytes;
                best = Math.Max(best ?? 0, bytes.Value);
            }
            return best;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Write($"GPU memory registry read failed: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<PartSpecs> Collect(string? gpuName, PhysicalDriveInfo? driveC, PhysicalDriveInfo? driveD,
        string? networkName, string? boardFromLhm, string[] superIo)
    {
        var list = new List<PartSpecs>();

        // CPU
        Dictionary<string, object?>? cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor").FirstOrDefault();
        list.Add(new PartSpecs(PartKind.Cpu, cpu is null
            ? new[] { new SpecLine("Model", "n/a (WMI query failed)") }
            : new[]
            {
                new SpecLine("Model", cpu.Str("Name") ?? "n/a"),
                new SpecLine("Cores / threads", $"{cpu.ULong("NumberOfCores")} / {cpu.ULong("NumberOfLogicalProcessors")}"),
                // Win32_Processor.MaxClockSpeed reports the rated (base) clock on AMD desktop CPUs.
                new SpecLine("Base clock", Mhz(cpu.ULong("MaxClockSpeed"))),
                new SpecLine("Boost clock", "n/a (Windows does not report it; see 'Clock (max core)')"),
                new SpecLine("L3 cache", cpu.ULong("L3CacheSize") is > 0 and var l3 ? Formatter.FormatBytes(l3 * 1024.0).ToString() : "n/a"),
                new SpecLine("Socket", cpu.Str("SocketDesignation") ?? "n/a"),
            }));

        // GPU
        List<Dictionary<string, object?>> gpus = Wmi.Query("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
        Dictionary<string, object?>? gpuRow = gpus.FirstOrDefault(g => gpuName != null && string.Equals(g.Str("Name"), gpuName, StringComparison.OrdinalIgnoreCase))
                                              ?? gpus.FirstOrDefault(g => (g.Str("Name") ?? "").Contains("RX", StringComparison.OrdinalIgnoreCase))
                                              ?? gpus.FirstOrDefault();
        double? vram = GpuMemoryBytes(gpuName ?? gpuRow?.Str("Name"));
        list.Add(new PartSpecs(PartKind.Gpu, new[]
        {
            new SpecLine("Model", gpuName ?? gpuRow?.Str("Name") ?? "n/a"),
            new SpecLine("Memory", vram is > 0 ? Formatter.FormatBytes(vram.Value).ToString() : "n/a (not in driver registry)"),
            new SpecLine("Driver", gpuRow?.Str("DriverVersion") ?? "n/a"),
        }));

        // RAM
        List<Dictionary<string, object?>> sticks = Wmi.Query("SELECT Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, DeviceLocator, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        ulong? slots = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").Select(r => r.ULong("MemoryDevices")).Where(v => v.HasValue).Sum(v => (long)v!.Value) is long s and > 0 ? (ulong)s : null;
        var ram = new List<SpecLine>();
        ulong total = (ulong)sticks.Sum(x => (decimal)(x.ULong("Capacity") ?? 0));
        ram.Add(new SpecLine("Installed", total > 0 ? Formatter.FormatBytes(total).ToString() : "n/a"));
        ram.Add(new SpecLine("Slots", slots.HasValue ? $"{sticks.Count} of {slots} used" : $"{sticks.Count} used (total slots not reported)"));
        ulong? rated = sticks.Select(x => x.ULong("Speed")).Max();
        ulong? configured = sticks.Select(x => x.ULong("ConfiguredClockSpeed")).Max();
        ram.Add(new SpecLine("Speed", configured is > 0 ? $"{configured} MT/s configured" + (rated is > 0 && rated != configured ? $" (rated {rated})" : "") : "n/a"));
        foreach (Dictionary<string, object?> stick in sticks)
            ram.Add(new SpecLine(stick.Str("DeviceLocator") ?? "DIMM",
                $"{Size(stick.ULong("Capacity"))} {MemoryType(stick.ULong("SMBIOSMemoryType"))} {stick.Str("Manufacturer")} {stick.Str("PartNumber")}".Trim()));
        list.Add(new PartSpecs(PartKind.Ram, ram));

        // Drives
        foreach ((PartKind kind, PhysicalDriveInfo? d) in new[] { (PartKind.DriveC, driveC), (PartKind.DriveD, driveD) })
        {
            list.Add(new PartSpecs(kind, d is null
                ? new[] { new SpecLine("Model", "n/a (no physical disk found for this letter)") }
                : new[]
                {
                    new SpecLine("Model", d.Model),
                    new SpecLine("Capacity", Size(d.Size)),
                    new SpecLine("Interface", d.Interface ?? "n/a"),
                    new SpecLine("Firmware", d.Firmware ?? "n/a"),
                    new SpecLine("Disk #", d.DiskIndex.ToString(Inv)),
                }));
        }

        // Motherboard
        Dictionary<string, object?>? board = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        Dictionary<string, object?>? bios = Wmi.Query("SELECT SMBIOSBIOSVersion, Manufacturer FROM Win32_BIOS").FirstOrDefault();
        list.Add(new PartSpecs(PartKind.Motherboard, new[]
        {
            new SpecLine("Board", board is null ? boardFromLhm ?? "n/a" : $"{board.Str("Manufacturer")} {board.Str("Product")}".Trim()),
            new SpecLine("BIOS", bios?.Str("SMBIOSBIOSVersion") ?? "n/a"),
            new SpecLine("Sensor chip", superIo.Length > 0 ? string.Join(", ", superIo) : "n/a (none detected)"),
        }));

        // Network (local API only)
        NetworkInterface? nic = LhmSensorProvider.FindInterface(networkName);
        list.Add(new PartSpecs(PartKind.Network, new[]
        {
            new SpecLine("Adapter", nic?.Description ?? networkName ?? "n/a"),
            new SpecLine("Link speed", nic is { Speed: > 0 } ? FormatLink(nic.Speed) : "n/a"),
            new SpecLine("Type", nic?.NetworkInterfaceType.ToString() ?? "n/a"),
        }));
        return list;
    }

    private static string FormatLink(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000_000
            ? $"{(bitsPerSecond / 1e9).ToString("0.#", Inv)} Gbit/s"
            : $"{(bitsPerSecond / 1e6).ToString("0", Inv)} Mbit/s";
}
