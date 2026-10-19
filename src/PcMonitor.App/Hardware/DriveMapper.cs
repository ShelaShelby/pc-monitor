using System.Globalization;
using PcMonitor.Core;

namespace PcMonitor.App.Hardware;

internal sealed record PhysicalDriveInfo(string Letter, int DiskIndex, string Model, string? Interface, string? Firmware, ulong? Size);

/// <summary>Maps drive letters (C:, D:) to the physical disk behind them, via WMI associations.</summary>
internal static class DriveMapper
{
    public static PhysicalDriveInfo? ForLetter(string letter)
    {
        foreach (Dictionary<string, object?> partition in Wmi.Query(
                     $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
        {
            string? partitionId = partition.Str("DeviceID");
            if (partitionId is null)
                continue;
            foreach (Dictionary<string, object?> disk in Wmi.Query(
                         $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId.Replace("'", "\\'")}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
            {
                int index = (int)(disk.ULong("Index") ?? 0);
                return new PhysicalDriveInfo(
                    letter,
                    index,
                    disk.Str("Model") ?? "unknown model",
                    StorageBus.InterfaceName(BusType(index), disk.Str("InterfaceType")),
                    disk.Str("FirmwareRevision"),
                    disk.ULong("Size"));
            }
        }
        return null;
    }

    /// <summary>
    /// MSFT_PhysicalDisk.BusType of disk number <paramref name="diskIndex"/> (DeviceId is the disk number as text).
    /// Null when the Storage namespace query fails or has no match; the caller then keeps the Win32_DiskDrive value.
    /// </summary>
    private static int? BusType(int diskIndex)
    {
        try
        {
            string id = diskIndex.ToString(CultureInfo.InvariantCulture);
            Dictionary<string, object?>? row = Wmi.Query("SELECT DeviceId, BusType FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage")
                .FirstOrDefault(r => r.Str("DeviceId") == id);
            return row?.ULong("BusType") is ulong bus and <= int.MaxValue ? (int)bus : null;
        }
        catch (Exception ex)
        {
            Log.Write($"MSFT_PhysicalDisk query failed: {ex.Message}");
            return null;
        }
    }
}
