using System.Windows;
using System.Windows.Controls;
using PcMonitor.Core;

namespace PcMonitor.App.Controls;

/// <summary>
/// Columns of equal width; children keep their order and each goes into the currently shortest
/// column (ties: leftmost), using <see cref="LayoutFit.Pack"/> so the panel and the fit plan agree.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(CardMetrics.MinColumnWidth, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    public double ColumnWidth { get => (double)GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }

    private int EffectiveColumns => Math.Max(1, Columns);

    private double EffectiveWidth => double.IsFinite(ColumnWidth) && ColumnWidth > 0 ? ColumnWidth : CardMetrics.MinColumnWidth;

    private double[] Heights()
    {
        var heights = new double[InternalChildren.Count];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = InternalChildren[i].DesiredSize.Height;
        return heights;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(EffectiveWidth, double.PositiveInfinity));
        (_, double[] sums) = LayoutFit.Pack(Heights(), EffectiveColumns);
        return new Size(EffectiveColumns * EffectiveWidth, sums.Length == 0 ? 0 : sums.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double[] heights = Heights();
        (int[] columnOf, _) = LayoutFit.Pack(heights, EffectiveColumns);
        var top = new double[EffectiveColumns];
        for (int i = 0; i < heights.Length; i++)
        {
            int c = columnOf[i];
            InternalChildren[i].Arrange(new Rect(c * EffectiveWidth, top[c], EffectiveWidth, heights[i]));
            top[c] += heights[i];
        }
        return finalSize;
    }
}
