namespace PcMonitor.Core;

public static class StorageBus
{
    /// <summary>MSFT_PhysicalDisk.BusType for NVMe (Microsoft MSFT_Disk / MSFT_PhysicalDisk docs).</summary>
    public const int NVMe = 17;

    /// <summary>
    /// Interface shown for a drive. Win32_DiskDrive reports NVMe drives as "SCSI", so the BusType of
    /// MSFT_PhysicalDisk wins when it says NVMe; any other or missing BusType keeps what Win32_DiskDrive reported.
    /// </summary>
    public static string? InterfaceName(int? busType, string? win32Interface) =>
        busType == NVMe ? "NVMe" : win32Interface;
}
