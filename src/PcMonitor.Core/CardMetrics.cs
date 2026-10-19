namespace PcMonitor.Core;

/// <summary>
/// Sizes of the card template in MainWindow.xaml (logical px, before the layout scale). The XAML reads
/// them with x:Static, and the UI tests use them to check that no text is wider than its column.
/// </summary>
public static class CardMetrics
{
    /// <summary>Narrowest column (card plus its margins); Fit stretches columns wider to fill the window.</summary>
    public const double MinColumnWidth = 430;

    public const double CardMargin = 8;
    public const double CompactCardMargin = 4;
    public const double CardBorder = 1;
    public const double CardPaddingX = 18;
    public const double CompactCardPaddingX = 14;

    // Metric rows: [label][gap][value + unit][gap][min/max]
    public const double LabelFontSize = 15;
    public const double ValueFontSize = 17;
    public const double UnitFontSize = 13;
    public const double MinMaxFontSize = 12;
    public const double LabelMinWidth = 90;
    /// <summary>Longer labels wrap instead of pushing the value out of the card.</summary>
    public const double LabelMaxWidth = 170;
    public const double ValueMinWidth = 64;
    public const double ColumnGap = 14;
    /// <summary>Space between a value and its unit (rows and heroes use the same idea, see HeroUnitGap).</summary>
    public const double UnitGap = 4;

    // Hero metrics: two equal columns, each with a right gap.
    public const double HeroValueFontSize = 44;
    public const double HeroUnitFontSize = 20;
    public const double HeroUnitGap = 6;
    public const double HeroMinMaxFontSize = 13;
    public const double HeroGap = 12;

    /// <summary>Width inside the card border and padding for a column of <paramref name="columnWidth"/>.</summary>
    public static double ContentWidth(double columnWidth, bool compact) =>
        columnWidth - 2 * ((compact ? CompactCardMargin : CardMargin) + CardBorder + (compact ? CompactCardPaddingX : CardPaddingX));

    /// <summary>Width available to one hero metric.</summary>
    public static double HeroWidth(double contentWidth) => contentWidth / 2 - HeroGap;
}
