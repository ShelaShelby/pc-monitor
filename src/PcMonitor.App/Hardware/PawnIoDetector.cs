using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PcMonitor.Core;

namespace PcMonitor.App.Hardware;

/// <summary>
/// Detects the PawnIO driver (https://pawnio.eu) the same way LibreHardwareMonitor does:
/// its uninstall registry entry plus opening the \\?\GLOBALROOT\Device\PawnIO device.
/// Unlike LibreHardwareMonitor's static check this can be repeated (Recheck button).
/// </summary>
internal static class PawnIoDetector
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";

    public static DriverStatus Detect()
    {
        string? version = ReadVersion();
        bool deviceOpens = TryOpenDevice(out int error);
        if (deviceOpens)
            return new DriverStatus(DriverState.Ready, version is null ? "PawnIO device available" : $"PawnIO {version}");
        if (version is not null)
            return new DriverStatus(DriverState.InstalledNotLoaded,
                $"PawnIO {version} is installed but its device could not be opened (error {error}). Reboot, or run as administrator.");
        return new DriverStatus(DriverState.Missing, "PawnIO driver is not installed.");
    }

    private static string? ReadVersion()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(UninstallKey);
                if (key?.GetValue("DisplayVersion") is string v && !string.IsNullOrWhiteSpace(v))
                    return v.Trim();
                if (key is not null)
                    return "(unknown version)";
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Write($"PawnIO registry check ({view}) failed: {ex.Message}");
            }
        }
        return null;
    }

    private static bool TryOpenDevice(out int error)
    {
        error = 0;
        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareRw = 0x3, OpenExisting = 3;
        using SafeFileHandle handle = CreateFile(DevicePath, GenericRead | GenericWrite, ShareRw, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
