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
                return new PhysicalDriveInfo(
                    letter,
                    (int)(disk.ULong("Index") ?? 0),
                    disk.Str("Model") ?? "unknown model",
                    disk.Str("InterfaceType"),
                    disk.Str("FirmwareRevision"),
                    disk.ULong("Size"));
            }
        }
        return null;
    }
}
