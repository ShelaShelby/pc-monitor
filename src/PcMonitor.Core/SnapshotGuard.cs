namespace PcMonitor.Core;

/// <summary>Cleans provider output before the tracker and the cards see it.</summary>
public static class SnapshotGuard
{
    /// <summary>
    /// One part per kind (the first wins) and unique metric keys within a part. Two sensors with the
    /// same name would otherwise share one history and one min/max, mixing their values; the second
    /// one gets the key "key#2" (then #3, ...) and keeps its label.
    /// </summary>
    public static SystemSnapshot Sanitize(SystemSnapshot snapshot)
    {
        var parts = new List<PartSnapshot>(snapshot.Parts.Count);
        var kinds = new HashSet<PartKind>();
        bool changed = false;
        foreach (PartSnapshot part in snapshot.Parts)
        {
            if (!kinds.Add(part.Kind))
            {
                changed = true;
                continue;
            }
            PartSnapshot clean = UniqueKeys(part);
            changed |= !ReferenceEquals(clean, part);
            parts.Add(clean);
        }
        return changed ? snapshot with { Parts = parts } : snapshot;
    }

    private static PartSnapshot UniqueKeys(PartSnapshot part)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        List<Metric>? renamed = null;
        for (int i = 0; i < part.Metrics.Count; i++)
        {
            Metric m = part.Metrics[i];
            string key = m.Key;
            for (int n = 2; !used.Add(key); n++)
                key = $"{m.Key}#{n}";
            if (key != m.Key)
            {
                renamed ??= part.Metrics.Take(i).ToList();
                renamed.Add(m with { Key = key });
            }
            else
                renamed?.Add(m);
        }
        return renamed is null ? part : part with { Metrics = renamed };
    }
}
