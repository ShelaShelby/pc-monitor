namespace PcMonitor.Core;

/// <summary>How the cards are arranged in the window.</summary>
public enum LayoutMode
{
    /// <summary>Scale the card grid so every card is visible without scrolling.</summary>
    Fit,
    Columns2,
    Columns3,
    Columns4,
    /// <summary>As many ~430 px columns as fit, wrapping, unscaled (0.1 behaviour).</summary>
    Auto,
}

/// <param name="Scrolls">True when the grid is taller than the window and needs a vertical scrollbar.</param>
public readonly record struct FitResult(int Columns, int Rows, double Scale, bool Scrolls);

public static class LayoutFit
{
    /// <summary>Fit never shrinks the cards below this; it scrolls vertically instead.</summary>
    public const double MinScale = 0.55;

    public static readonly LayoutMode[] Modes = { LayoutMode.Fit, LayoutMode.Columns2, LayoutMode.Columns3, LayoutMode.Columns4, LayoutMode.Auto };

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

    private static int RowsFor(int count, int columns) => (count + columns - 1) / columns;

    /// <summary>
    /// Picks the column count (1..cardCount) that gives the largest uniform scale for a grid of
    /// equal cells of cardNaturalW x cardNaturalH in a windowW x windowH area. Ties go to fewer columns.
    /// If even the best scale is below <see cref="MinScale"/>, the scale stays at MinScale, the columns
    /// are as many as fit the width at that scale, and the result scrolls vertically.
    /// </summary>
    public static FitResult ChooseFit(double windowW, double windowH, double cardNaturalW, double cardNaturalH, int cardCount)
    {
        if (cardCount < 1 || !Valid(windowW, windowH, cardNaturalW, cardNaturalH))
            return new FitResult(1, Math.Max(cardCount, 0), 1, false);

        int bestColumns = 1;
        double bestScale = 0;
        for (int columns = 1; columns <= cardCount; columns++)
        {
            int rows = RowsFor(cardCount, columns);
            double scale = Math.Min(windowW / (columns * cardNaturalW), windowH / (rows * cardNaturalH));
            if (scale > bestScale + 1e-12)
            {
                bestScale = scale;
                bestColumns = columns;
            }
        }
        if (bestScale >= MinScale)
            return new FitResult(bestColumns, RowsFor(cardCount, bestColumns), bestScale, false);

        int fitColumns = Math.Clamp((int)Math.Floor(windowW / (cardNaturalW * MinScale)), 1, cardCount);
        return new FitResult(fitColumns, RowsFor(cardCount, fitColumns), MinScale, true);
    }

    /// <summary>Fixed column count: scale so the columns fill the width; scroll vertically if taller than the window.</summary>
    public static FitResult ForColumns(double windowW, double windowH, double cardNaturalW, double cardNaturalH, int cardCount, int columns)
    {
        if (cardCount < 1 || !Valid(windowW, windowH, cardNaturalW, cardNaturalH))
            return new FitResult(1, Math.Max(cardCount, 0), 1, false);
        columns = Math.Clamp(columns, 1, cardCount);
        int rows = RowsFor(cardCount, columns);
        double scale = windowW / (columns * cardNaturalW);
        return new FitResult(columns, rows, scale, rows * cardNaturalH * scale > windowH);
    }

    /// <summary>
    /// Layout for a scaled mode. When the grid scrolls, the vertical scrollbar takes
    /// <paramref name="scrollbarWidth"/> off the width, so the plan is redone for the narrower area.
    /// </summary>
    public static FitResult Plan(LayoutMode mode, double windowW, double windowH, double cardNaturalW, double cardNaturalH, int cardCount, double scrollbarWidth)
    {
        FitResult Compute(double w) => ColumnsOf(mode) is int c
            ? ForColumns(w, windowH, cardNaturalW, cardNaturalH, cardCount, c)
            : ChooseFit(w, windowH, cardNaturalW, cardNaturalH, cardCount);

        FitResult r = Compute(windowW);
        if (r.Scrolls && scrollbarWidth > 0 && windowW - scrollbarWidth > 0)
            r = Compute(windowW - scrollbarWidth) with { Scrolls = true };
        return r;
    }
}
