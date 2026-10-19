namespace PcMonitor.Core;

public readonly record struct MemoryStick(string? DeviceLocator, string? BankLabel);

public static class RamLabels
{
    /// <summary>
    /// "Stick n: DeviceLocator / BankLabel" per module, numbered in the order Windows lists them.
    /// Some boards report the same DeviceLocator for every stick, so the number keeps labels unique.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyList<MemoryStick> sticks)
    {
        var labels = new string[sticks.Count];
        for (int i = 0; i < sticks.Count; i++)
        {
            string[] parts = new[] { sticks[i].DeviceLocator, sticks[i].BankLabel }
                .Select(p => p?.Trim() ?? "")
                .Where(p => p.Length > 0)
                .ToArray();
            string number = $"Stick {i + 1}";
            labels[i] = parts.Length == 0 ? number : $"{number}: {string.Join(" / ", parts)}";
        }
        return labels;
    }
}
