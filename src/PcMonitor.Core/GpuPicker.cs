using System.Text.RegularExpressions;

namespace PcMonitor.Core;

public enum GpuVendor
{
    Amd,
    Nvidia,
    Intel,
}

/// <param name="DedicatedMemoryMb">Dedicated VRAM as reported by the driver, or null when not reported.</param>
public sealed record GpuCandidate(string Name, GpuVendor Vendor, double? DedicatedMemoryMb);

/// <summary>Chooses the GPU for the GPU card when several are present (integrated + discrete).</summary>
public static class GpuPicker
{
    // Integrated Radeon names: "AMD Radeon(TM) Graphics" (Ryzen 7000/9000 desktop), "Radeon Vega 8 Graphics",
    // "AMD Radeon 780M Graphics" (mobile). Discrete cards carry an RX/PRO model name.
    private static readonly Regex IntegratedRadeon = new(
        @"^(AMD\s+)?Radeon(\(TM\))?(\s+Vega\b.*|\s+\d{3,4}M\b.*|\s+Graphics)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsIntegrated(GpuCandidate gpu)
    {
        string name = gpu.Name.Trim();
        return gpu.Vendor switch
        {
            GpuVendor.Amd => IntegratedRadeon.IsMatch(name),
            // Intel discrete cards are "Arc A/B..." (the integrated "Arc Graphics" of Core Ultra has no model number).
            GpuVendor.Intel => !Regex.IsMatch(name, @"\bArc(\(TM\))?\s+[AB]\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => false,
        };
    }

    /// <summary>
    /// Index of the GPU to show, or null for an empty list: discrete before integrated, then the most
    /// dedicated memory, then AMD (the ADL fallback only works on AMD), then the first listed.
    /// </summary>
    public static int? Pick(IReadOnlyList<GpuCandidate> gpus)
    {
        if (gpus.Count == 0)
            return null;
        return Enumerable.Range(0, gpus.Count)
            .OrderBy(i => IsIntegrated(gpus[i]))
            .ThenByDescending(i => gpus[i].DedicatedMemoryMb is double mb && double.IsFinite(mb) ? mb : 0)
            .ThenByDescending(i => gpus[i].Vendor == GpuVendor.Amd)
            .ThenBy(i => i)
            .First();
    }
}
