using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PcMonitor.App.Hardware;

/// <summary>
/// Fallback reader for AMD GPU values LibreHardwareMonitor does not expose. Talks to the AMD
/// driver's own ADL library (atiadlxx.dll, installed with Adrenalin) via ADL2_New_QueryPMLogData_Get.
/// Read-only: nothing is ever written to the GPU.
/// </summary>
internal sealed class AdlFallback : IDisposable
{
    // ADL_PMLOG_* sensor ids from AMD's adl_defines.h (GPUOpen display-library).
    public const int ClkGfx = 1, ClkMem = 2, TempEdge = 8, TempMem = 9, FanRpm = 14, FanPercent = 15,
        ActivityGfx = 19, AsicPower = 23, TempHotspot = 27, TempGfx = 28, BoardPower = 73;

    private const string Dll = "atiadlxx.dll";
    private const int MaxSensors = 256;
    private const int AdlMaxPath = 256;

    private IntPtr _context;
    private readonly int _adapterIndex;
    private static readonly AllocDelegate Alloc = Marshal.AllocHGlobal; // kept alive for the native callback

    private AdlFallback(IntPtr context, int adapterIndex, string adapterName)
    {
        _context = context;
        _adapterIndex = adapterIndex;
        AdapterName = adapterName;
    }

    public string AdapterName { get; }

    /// <summary>Returns null with a reason when ADL is not usable (no AMD driver, no PMLog support).</summary>
    public static AdlFallback? TryCreate(string? preferredName, out string reason)
    {
        IntPtr context = IntPtr.Zero;
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, ref context) != 0 || context == IntPtr.Zero)
            {
                reason = "ADL init failed";
                return null;
            }
            int count = 0;
            if (ADL2_Adapter_NumberOfAdapters_Get(context, ref count) != 0 || count <= 0)
            {
                ADL2_Main_Control_Destroy(context);
                reason = "ADL found no adapters";
                return null;
            }

            int size = Marshal.SizeOf<AdapterInfo>();
            IntPtr buffer = Marshal.AllocHGlobal(size * count);
            var candidates = new List<(int Index, string Name)>();
            try
            {
                if (ADL2_Adapter_AdapterInfo_Get(context, buffer, size * count) == 0)
                {
                    var seenBus = new HashSet<int>();
                    for (int i = 0; i < count; i++)
                    {
                        AdapterInfo info = Marshal.PtrToStructure<AdapterInfo>(buffer + i * size);
                        Match m = Regex.Match(info.UDID ?? "", "PCI_VEN_([A-Fa-f0-9]{1,4})");
                        bool amd = m.Success && Convert.ToInt32(m.Groups[1].Value, 16) == 0x1002;
                        if (!amd || info.Present == 0 || !seenBus.Add(info.BusNumber))
                            continue;
                        candidates.Add((info.AdapterIndex, (info.AdapterName ?? "").Trim()));
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            // Prefer the adapter LibreHardwareMonitor picked, then discrete-looking names.
            IEnumerable<(int Index, string Name)> ordered = candidates
                .OrderByDescending(c => preferredName != null && c.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Name.Contains("RX", StringComparison.OrdinalIgnoreCase));
            foreach ((int index, string name) in ordered)
            {
                var data = new PmLogDataOutput { Sensors = new SingleSensorData[MaxSensors] };
                if (ADL2_New_QueryPMLogData_Get(context, index, ref data) == 0)
                {
                    reason = "";
                    return new AdlFallback(context, index, name);
                }
            }
            ADL2_Main_Control_Destroy(context);
            reason = candidates.Count == 0 ? "ADL found no AMD adapter" : "ADL PMLog not supported by driver";
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            reason = "AMD ADL library not available";
            return null;
        }
    }

    /// <summary>Sensor id → value for every sensor the driver reports as supported.</summary>
    public IReadOnlyDictionary<int, int> Query()
    {
        var result = new Dictionary<int, int>();
        if (_context == IntPtr.Zero)
            return result;
        try
        {
            var data = new PmLogDataOutput { Sensors = new SingleSensorData[MaxSensors] };
            if (ADL2_New_QueryPMLogData_Get(_context, _adapterIndex, ref data) != 0 || data.Sensors is null)
                return result;
            for (int i = 1; i < data.Sensors.Length; i++)
            {
                if (data.Sensors[i].Supported != 0)
                    result[i] = data.Sensors[i].Value;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"ADL query failed: {ex.Message}");
        }
        return result;
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
        {
            try { ADL2_Main_Control_Destroy(_context); } catch (Exception ex) { Log.Write($"ADL destroy: {ex.Message}"); }
            _context = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AllocDelegate(int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct SingleSensorData
    {
        public int Supported;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PmLogDataOutput
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxSensors)]
        public SingleSensorData[] Sensors;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string PNPString;
        public int OSDisplayIndex;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ADL2_Main_Control_Create(AllocDelegate callback, int connectedAdapters, ref IntPtr context);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, ref int count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int size);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int ADL2_New_QueryPMLogData_Get(IntPtr context, int adapterIndex, ref PmLogDataOutput data);
}
