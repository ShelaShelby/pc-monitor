using System.Globalization;
using System.Management;

namespace PcMonitor.App.Hardware;

/// <summary>Small, failure-tolerant WMI helpers. Every query is local (root\cimv2); no network.</summary>
internal static class Wmi
{
    public static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, wql);
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject o in results)
            {
                using (o)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData p in o.Properties)
                        row[p.Name] = p.Value;
                    rows.Add(row);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            Log.Write($"WMI query failed ({wql}): {ex.Message}");
        }
        return rows;
    }

    public static string? Str(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() : null;

    public static ulong? ULong(this Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out object? v) || v is null)
            return null;
        try
        {
            return Convert.ToUInt64(v, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
