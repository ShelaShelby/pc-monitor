using System.Runtime.InteropServices;
using PcMonitor.Core;

namespace PcMonitor.App;

internal static class NativeMethods
{
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    private static MONITORINFOEX? Info(IntPtr monitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
        return GetMonitorInfo(monitor, ref info) ? info : null;
    }

    /// <summary>Work areas of all attached monitors, in physical screen pixels.</summary>
    public static IReadOnlyList<ScreenRect> MonitorWorkAreas()
    {
        var list = new List<ScreenRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (Info(m) is { } i)
                list.Add(new ScreenRect(i.rcWork.Left, i.rcWork.Top, i.rcWork.Right - i.rcWork.Left, i.rcWork.Bottom - i.rcWork.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string? MonitorName(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? null : Info(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST))?.szDevice;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    /// <summary>Same attribute on Windows 10 builds before 20H1.</summary>
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    /// <summary>Dark title bar to match the app. Best effort: any failure keeps the default title bar.</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows without these DWM attributes: keep the default title bar.
        }
    }
}
