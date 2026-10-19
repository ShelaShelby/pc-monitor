namespace PcMonitor.Core;

/// <summary>How the cards are arranged in the window.</summary>
public enum LayoutMode
{
    /// <summary>Scale the packed columns so every card is visible without scrolling.</summary>
    Fit,
    Columns2,
    Columns3,
    Columns4,
    /// <summary>As many ~430 px columns as fit, wrapping, unscaled (0.1 behaviour).</summary>
    Auto,
}

public sealed record LayoutOption(LayoutMode Mode, string Name)
{
    /// <summary>The name, so any control that shows the item as text shows the mode name (not the record dump).</summary>
    public override string ToString() => Name;
}

/// <param name="Columns">Number of columns.</param>
/// <param name="Scale">Uniform scale applied to the whole card area.</param>
/// <param name="ColumnWidth">Logical (unscaled) width of one column, including the card margins; never below the minimum.</param>
/// <param name="TallestColumn">Logical height of the tallest packed column.</param>
/// <param name="Scrolls">True when the columns are taller than the window and need a vertical scrollbar.</param>
/// <param name="ColumnOf">Column index of each card, in card order.</param>
public sealed record FitResult(int Columns, double Scale, double ColumnWidth, double TallestColumn, bool Scrolls, IReadOnlyList<int> ColumnOf);

public static class LayoutFit
{
    /// <summary>Fit never shrinks the cards below this; it scrolls vertically instead.</summary>
    public const double MinScale = 0.55;

    public static readonly LayoutMode[] Modes = { LayoutMode.Fit, LayoutMode.Columns2, LayoutMode.Columns3, LayoutMode.Columns4, LayoutMode.Auto };

    /// <summary>Names shown in the Layout dropdown, one per mode, in hotkey order.</summary>
    public static readonly IReadOnlyList<LayoutOption> Options = new[]
    {
        new LayoutOption(LayoutMode.Fit, "Fit to window"),
        new LayoutOption(LayoutMode.Columns2, "2 columns"),
        new LayoutOption(LayoutMode.Columns3, "3 columns"),
        new LayoutOption(LayoutMode.Columns4, "4 columns"),
        new LayoutOption(LayoutMode.Auto, "Auto (wrap)"),
    };

    public static LayoutMode Next(LayoutMode mode)
    {
        int i = Array.IndexOf(Modes, mode);
        return Modes[(i + 1) % Modes.Length];
    }

    public static int? ColumnsOf(LayoutMode mode) => mode switch
    {
        LayoutMode.Columns2 => 2,
        LayoutMode.Columns3 => 3,
        LayoutMode.Columns4 => 4,
        _ => null,
    };

    private static bool Valid(params double[] v) => v.All(x => double.IsFinite(x) && x > 0);

    /// <summary>
    /// Masonry packing: cards keep their order, each goes into the currently shortest column
    /// (ties: leftmost). Returns the column of each card and the height of each column.
    /// </summary>
    public static (int[] ColumnOf, double[] ColumnHeights) Pack(IReadOnlyList<double> heights, int columns)
    {
        columns = Math.Max(1, columns);
        var columnOf = new int[heights.Count];
        var sums = new double[columns];
        for (int i = 0; i < heights.Count; i++)
        {
            int shortest = 0;
            for (int c = 1; c < columns; c++)
                if (sums[c] < sums[shortest])
                    shortest = c;
            columnOf[i] = shortest;
            sums[shortest] += Math.Max(0, heights[i]);
        }
        return (columnOf, sums);
    }

    private static FitResult Result(IReadOnlyList<double> heights, int columns, double scale, double windowW, double windowH, double minColumnWidth)
    {
        (int[] columnOf, double[] sums) = Pack(heights, columns);
        double tallest = sums.Max();
        // Stretch the columns so they fill the window width at this scale (never narrower than the minimum).
        double columnWidth = Math.Max(minColumnWidth, windowW / (scale * columns));
        return new FitResult(columns, scale, columnWidth, tallest, tallest * scale > windowH + 1e-9, columnOf);
    }

    private static FitResult Empty(int count) => new(1, 1, 0, 0, false, new int[Math.Max(count, 0)]);

    private static bool ValidHeights(IReadOnlyList<double> heights) => heights.Count > 0 && heights.All(h => double.IsFinite(h) && h > 0);

    /// <summary>
    /// For each column count 1..n packs the cards and computes
    /// scale = min(windowW / (columns * minColumnWidth), windowH / tallestColumn); the largest scale wins
    /// (ties: fewer columns). Below <see cref="MinScale"/> the scale stays at MinScale, the columns are as
    /// many as fit the width at that scale, and the result scrolls vertically.
    /// </summary>
    public static FitResult ChooseFit(double windowW, double windowH, IReadOnlyList<double> heights, double minColumnWidth)
    {
        if (!ValidHeights(heights) || !Valid(windowW, windowH, minColumnWidth))
            return Empty(heights.Count);

        int bestColumns = 1;
        double bestScale = 0;
        for (int columns = 1; columns <= heights.Count; columns++)
        {
            double tallest = Pack(heights, columns).ColumnHeights.Max();
            double scale = Math.Min(windowW / (columns * minColumnWidth), windowH / tallest);
            if (scale > bestScale + 1e-12)
            {
                bestScale = scale;
                bestColumns = columns;
            }
        }
        if (bestScale >= MinScale)
            return Result(heights, bestColumns, bestScale, windowW, windowH, minColumnWidth);

        int fitColumns = Math.Clamp((int)Math.Floor(windowW / (minColumnWidth * MinScale)), 1, heights.Count);
        return Result(heights, fitColumns, MinScale, windowW, windowH, minColumnWidth) with { Scrolls = true };
    }

    /// <summary>Fixed column count: same packing, scaled so the columns fill the width; scrolls vertically if taller than the window.</summary>
    public static FitResult ForColumns(double windowW, double windowH, IReadOnlyList<double> heights, double minColumnWidth, int columns)
    {
        if (!ValidHeights(heights) || !Valid(windowW, windowH, minColumnWidth))
            return Empty(heights.Count);
        columns = Math.Clamp(columns, 1, heights.Count);
        return Result(heights, columns, windowW / (columns * minColumnWidth), windowW, windowH, minColumnWidth);
    }

    /// <summary>
    /// Layout for a scaled mode. When the columns scroll, the vertical scrollbar takes
    /// <paramref name="scrollbarWidth"/> off the width, so the plan is redone for the narrower area.
    /// </summary>
    public static FitResult Plan(LayoutMode mode, double windowW, double windowH, IReadOnlyList<double> heights, double minColumnWidth, double scrollbarWidth)
    {
        FitResult Compute(double w) => ColumnsOf(mode) is int c
            ? ForColumns(w, windowH, heights, minColumnWidth, c)
            : ChooseFit(w, windowH, heights, minColumnWidth);

        FitResult r = Compute(windowW);
        if (r.Scrolls && scrollbarWidth > 0 && windowW - scrollbarWidth > 0)
            r = Compute(windowW - scrollbarWidth) with { Scrolls = true };
        return r;
    }
}
