using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class LayoutFitTests
{
    // Expected values worked out by hand: for each column count c, rows = ceil(n / c) and
    // scale = min(W / (c * cardW), H / (rows * cardH)); the largest scale wins.

    [Fact]
    public void WideWindowSevenCards()
    {
        // 1920x1080, cards 450x600:
        // c=1 min(4.267, 0.257) c=2 min(2.133, 0.45) c=3 min(1.422, 0.6) c=4 min(1.067, 0.9)
        // c=5 min(0.853, 0.9) c=6 min(0.711, 0.9) c=7 min(0.610, 1.8)  -> 4 columns, 2 rows, 0.9
        FitResult r = LayoutFit.ChooseFit(1920, 1080, 450, 600, 7);
        Assert.Equal(4, r.Columns);
        Assert.Equal(2, r.Rows);
        Assert.Equal(0.9, r.Scale, 9);
        Assert.False(r.Scrolls);
    }

    [Fact]
    public void TallWindowSevenCards()
    {
        // 800x1600, cards 400x500:
        // c=1 min(2, 0.457) c=2 min(1, 0.8) c=3 min(0.667, 1.067) c=4 min(0.5, 1.6) ... -> 2 columns, 4 rows, 0.8
        FitResult r = LayoutFit.ChooseFit(800, 1600, 400, 500, 7);
        Assert.Equal(2, r.Columns);
        Assert.Equal(4, r.Rows);
        Assert.Equal(0.8, r.Scale, 9);
        Assert.False(r.Scrolls);
    }

    [Fact]
    public void TinyWindowHitsTheFloor()
    {
        // 480x300, cards 400x500: best is c=4 min(0.3, 0.3) = 0.3 < 0.55.
        // Floor: scale 0.55, columns = floor(480 / (400 * 0.55)) = floor(2.18) = 2, rows = 4, scrolls.
        FitResult r = LayoutFit.ChooseFit(480, 300, 400, 500, 7);
        Assert.Equal(2, r.Columns);
        Assert.Equal(4, r.Rows);
        Assert.Equal(0.55, r.Scale, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void OneCard()
    {
        // 1000x800, card 400x500: c=1 min(2.5, 1.6) = 1.6.
        FitResult r = LayoutFit.ChooseFit(1000, 800, 400, 500, 1);
        Assert.Equal(1, r.Columns);
        Assert.Equal(1, r.Rows);
        Assert.Equal(1.6, r.Scale, 9);
        Assert.False(r.Scrolls);
    }

    [Fact]
    public void FixedColumnsScaleOnWidthOnly()
    {
        // 3 columns of 400 in 1000 wide: scale 1000/1200 = 0.8333; 3 rows * 500 * 0.8333 = 1250 > 800 -> scrolls.
        FitResult r = LayoutFit.ForColumns(1000, 800, 400, 500, 7, 3);
        Assert.Equal(3, r.Columns);
        Assert.Equal(3, r.Rows);
        Assert.Equal(1000.0 / 1200.0, r.Scale, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void ScrollingPlanLeavesRoomForTheScrollbar()
    {
        // As above, but the 20 px scrollbar leaves 980: scale 980/1200.
        FitResult r = LayoutFit.Plan(LayoutMode.Columns3, 1000, 800, 400, 500, 7, 20);
        Assert.Equal(980.0 / 1200.0, r.Scale, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void HotkeyCyclesThroughAllLayouts()
    {
        Assert.Equal(LayoutMode.Columns2, LayoutFit.Next(LayoutMode.Fit));
        Assert.Equal(LayoutMode.Auto, LayoutFit.Next(LayoutMode.Columns4));
        Assert.Equal(LayoutMode.Fit, LayoutFit.Next(LayoutMode.Auto));
    }
}
