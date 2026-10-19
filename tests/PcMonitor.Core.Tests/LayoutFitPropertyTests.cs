using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

/// <summary>Invariants of the layout plan over many random card heights and window sizes.</summary>
public class LayoutFitPropertyTests
{
    private const double MinW = CardMetrics.MinColumnWidth;

    private static double[] RandomHeights(Random rng) =>
        Enumerable.Range(0, rng.Next(1, 12)).Select(_ => 50 + rng.NextDouble() * 1500).ToArray();

    private static void AssertInvariants(FitResult r, double w, double h, double[] heights, LayoutMode mode)
    {
        Assert.Equal(heights.Length, r.ColumnOf.Count);
        Assert.InRange(r.Columns, 1, heights.Length);
        Assert.All(r.ColumnOf, c => Assert.InRange(c, 0, r.Columns - 1));
        Assert.True(double.IsFinite(r.Scale) && r.Scale > 0);
        Assert.True(r.ColumnWidth >= MinW - 1e-9);

        // Fit never goes below the floor (documented in LayoutFit.MinScale). Fixed column counts scale
        // to the width by design (Columns4 in a narrow window is smaller): no floor there.
        if (mode == LayoutMode.Fit)
            Assert.True(r.Scale >= LayoutFit.MinScale - 1e-12, $"scale {r.Scale}");

        // Columns never overlap and never exceed the window width: columns * columnWidth * scale <= width.
        Assert.True(r.Columns * r.ColumnWidth * r.Scale <= w + 1e-6 || r.ColumnWidth == MinW,
            $"{r.Columns} x {r.ColumnWidth} x {r.Scale} > {w}");

        // Packing matches the column assignment, and the tallest column is the reported one.
        var sums = new double[r.Columns];
        for (int i = 0; i < heights.Length; i++)
            sums[r.ColumnOf[i]] += heights[i];
        Assert.Equal(sums.Max(), r.TallestColumn, 6);

        // No vertical overflow without a scrollbar.
        if (!r.Scrolls)
            Assert.True(r.TallestColumn * r.Scale <= h + 1e-6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomInputsKeepTheInvariants(int seed)
    {
        var rng = new Random(seed);
        for (int i = 0; i < 3000; i++)
        {
            double[] heights = RandomHeights(rng);
            double w = 200 + rng.NextDouble() * 7000;
            double h = 150 + rng.NextDouble() * 4000;
            foreach (LayoutMode mode in new[] { LayoutMode.Fit, LayoutMode.Columns2, LayoutMode.Columns3, LayoutMode.Columns4 })
            {
                FitResult r = LayoutFit.Plan(mode, w, h, heights, MinW, 17);
                AssertInvariants(r, w, h, heights, mode);
                // Deterministic: the same input gives the same plan.
                FitResult again = LayoutFit.Plan(mode, w, h, heights, MinW, 17);
                Assert.Equal(r.Columns, again.Columns);
                Assert.Equal(r.Scale, again.Scale);
                Assert.Equal(r.ColumnWidth, again.ColumnWidth);
                Assert.Equal(r.ColumnOf, again.ColumnOf);
            }
        }
    }

    [Theory]
    [InlineData(double.NaN, 500)]
    [InlineData(500, double.NaN)]
    [InlineData(double.PositiveInfinity, 500)]
    [InlineData(-10, 500)]
    [InlineData(0, 0)]
    public void InvalidWindowSizesNeverThrow(double w, double h)
    {
        double[] heights = { 500, 600 };
        foreach (LayoutMode mode in LayoutFit.Modes)
        {
            FitResult r = LayoutFit.Plan(mode, w, h, heights, MinW, 17);
            Assert.Equal(1, r.Scale);
            Assert.Equal(2, r.ColumnOf.Count);
        }
    }

    [Fact]
    public void HugeWindowNeverOverflowsTheColumnCount()
    {
        // 1e15 px wide: the floor branch must not cast an out-of-range double to int.
        double[] heights = { 1e12, 1e12, 1e12 };
        FitResult r = LayoutFit.ChooseFit(1e15, 1, heights, MinW);
        Assert.InRange(r.Columns, 1, 3);
        Assert.Equal(LayoutFit.MinScale, r.Scale, 12);
        Assert.True(r.Scrolls);
    }

    [Theory]
    [InlineData(new double[] { }, 1)]
    [InlineData(new double[] { 0, 0 }, 2)]
    [InlineData(new double[] { -5, 100 }, 2)]
    [InlineData(new double[] { double.NaN, 100 }, 2)]
    public void BadCardHeightsGiveTheUnscaledFallback(double[] heights, int count)
    {
        FitResult r = LayoutFit.Plan(LayoutMode.Fit, 1600, 1000, heights, MinW, 17);
        Assert.Equal(1, r.Scale);
        Assert.Equal(heights.Length, r.ColumnOf.Count);
        Assert.True(count >= heights.Length);
    }

    [Fact]
    public void PackIsStableForRandomInput()
    {
        var rng = new Random(11);
        for (int i = 0; i < 2000; i++)
        {
            double[] heights = RandomHeights(rng);
            int columns = rng.Next(-2, 9);
            (int[] a, double[] sa) = LayoutFit.Pack(heights, columns);
            (int[] b, double[] sb) = LayoutFit.Pack(heights, columns);
            Assert.Equal(a, b);
            Assert.Equal(sa, sb);
            Assert.Equal(heights.Sum(), sa.Sum(), 6);
            Assert.All(a, c => Assert.InRange(c, 0, Math.Max(1, columns) - 1));
        }
    }
}
