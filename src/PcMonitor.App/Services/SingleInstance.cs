using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcMonitor.App;

/// <summary>Named mutex for the session; a second start brings the running window to the front instead.</summary>
internal static class SingleInstance
{
    private const string MutexName = "PcMonitor.SingleInstance.6F2B9C1E";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }
            _mutex = mutex;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // The name exists but cannot be opened (other user / integrity level): another instance owns it.
            Log.Write($"Single-instance mutex: {ex.Message}");
            return false;
        }
    }

    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more; disposing is enough.
        }
        _mutex?.Dispose();
        _mutex = null;
    }

    public static void ActivateOther()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (Process p in Process.GetProcessesByName(self.ProcessName))
            {
                using (p)
                {
                    if (p.Id == self.Id || p.MainWindowHandle == IntPtr.Zero)
                        continue;
                    if (IsIconic(p.MainWindowHandle))
                        ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Write($"Activating the running instance failed: {ex.Message}");
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
