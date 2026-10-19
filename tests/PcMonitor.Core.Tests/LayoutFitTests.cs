using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class LayoutFitTests
{
    // Seven cards in display order (CPU, GPU, RAM, Drive C, Drive D, Motherboard, Network), natural heights in px.
    private static readonly double[] Seven = { 560, 640, 500, 520, 520, 660, 400 };
    private const double MinW = 430;

    // Packing by hand (next card into the shortest column, ties leftmost), tallest column per column count:
    // c=1: 3800
    // c=2: 560|640 -> RAM col0 1060 -> C col1 1160 -> D col0 1580 -> MB col1 1820 -> Net col0 1980        tallest 1980
    // c=3: 560|640|500 -> C col2 1020 -> D col0 1080 -> MB col1 1300 -> Net col2 1420                     tallest 1420
    // c=4: 560|640|500|520 -> D col2 1020 -> MB col3 1180 -> Net col0 960                                 tallest 1180
    // c=5: 560|640|500|520|520 -> MB col2 1160 -> Net col3 (520, tie with col4, leftmost) 920             tallest 1160
    // c=6: 560|640|500|520|520|660 -> Net col2 900                                                       tallest 900
    // c=7: tallest 660

    [Fact]
    public void PackPutsEachCardIntoTheShortestColumn()
    {
        (int[] columnOf, double[] sums) = LayoutFit.Pack(Seven, 4);
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 3, 0 }, columnOf);
        Assert.Equal(new double[] { 960, 640, 1020, 1180 }, sums);
    }

    [Fact]
    public void PackTiesGoLeftmost()
    {
        (int[] columnOf, _) = LayoutFit.Pack(new double[] { 100, 100, 100, 100, 100 }, 2);
        Assert.Equal(new[] { 0, 1, 0, 1, 0 }, columnOf);
        (int[] five, _) = LayoutFit.Pack(Seven, 5);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 2, 3 }, five);
    }

    [Fact]
    public void FullHdSevenCards()
    {
        // 1920x1080, scale = min(1920 / (c * 430), 1080 / tallest):
        // c=1 min(4.465, 0.284) c=2 min(2.233, 0.545) c=3 min(1.488, 0.761) c=4 min(1.116, 0.915)
        // c=5 min(0.893, 0.931) = 0.893   c=6 min(0.744, 1.2)   c=7 min(0.638, 1.636)  -> 4 columns, 1080 / 1180.
        // Column width stretched to fill: 1920 / (0.91525 * 4) = 1920 * 1180 / (1080 * 4) = 524.44.
        FitResult r = LayoutFit.ChooseFit(1920, 1080, Seven, MinW);
        Assert.Equal(4, r.Columns);
        Assert.Equal(1080.0 / 1180.0, r.Scale, 9);
        Assert.Equal(1920.0 * 1180.0 / (1080.0 * 4), r.ColumnWidth, 9);
        Assert.Equal(1180, r.TallestColumn, 9);
        Assert.Equal(new[] { 0, 1, 2, 3, 2, 3, 0 }, r.ColumnOf);
        Assert.False(r.Scrolls);
        // The packed columns fill the whole width.
        Assert.Equal(1920, r.Columns * r.ColumnWidth * r.Scale, 6);
    }

    [Fact]
    public void Window1024x768SevenCards()
    {
        // c=1 min(2.381, 0.202) c=2 min(1.191, 0.388) c=3 min(0.794, 0.541) c=4 min(0.595, 0.651)
        // c=5 min(0.476, 0.662) c=6 min(0.397, 0.853) c=7 min(0.340, 1.164)
        // -> 4 columns, width-limited: 1024 / 1720 = 0.5953 (>= 0.55), column width stays 430.
        FitResult r = LayoutFit.ChooseFit(1024, 768, Seven, MinW);
        Assert.Equal(4, r.Columns);
        Assert.Equal(1024.0 / 1720.0, r.Scale, 9);
        Assert.Equal(430, r.ColumnWidth, 9);
        Assert.False(r.Scrolls);
    }

    [Fact]
    public void TinyWindowHitsTheFloor()
    {
        // 600x400: c=1 min(1.395, 0.105) c=2 min(0.698, 0.202) c=3 min(0.465, 0.282) c=4 min(0.349, 0.339)
        // c=5 0.279 c=6 0.233 c=7 0.199 -> best 0.339 < 0.55.
        // Floor: scale 0.55, columns = floor(600 / (430 * 0.55)) = floor(2.537) = 2, tallest 1980 (c=2 packing),
        // column width 600 / (0.55 * 2) = 545.45, scrolls.
        FitResult r = LayoutFit.ChooseFit(600, 400, Seven, MinW);
        Assert.Equal(2, r.Columns);
        Assert.Equal(0.55, r.Scale, 9);
        Assert.Equal(1980, r.TallestColumn, 9);
        Assert.Equal(600 / (0.55 * 2), r.ColumnWidth, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void EqualScalesPreferFewerColumns()
    {
        // Two 500 px cards in 860x1000: c=1 min(2, 1000/1000) = 1; c=2 min(860/860, 1000/500) = 1 -> tie, 1 column.
        FitResult r = LayoutFit.ChooseFit(860, 1000, new double[] { 500, 500 }, MinW);
        Assert.Equal(1, r.Columns);
        Assert.Equal(1, r.Scale, 9);
        // Column width fills: 860 / (1 * 1) = 860.
        Assert.Equal(860, r.ColumnWidth, 9);
    }

    [Fact]
    public void OneCard()
    {
        // 1000x800, card 500 tall: c=1 min(1000/430 = 2.326, 800/500 = 1.6) = 1.6; width 1000 / 1.6 = 625.
        FitResult r = LayoutFit.ChooseFit(1000, 800, new double[] { 500 }, MinW);
        Assert.Equal(1, r.Columns);
        Assert.Equal(1.6, r.Scale, 9);
        Assert.Equal(625, r.ColumnWidth, 9);
        Assert.False(r.Scrolls);
    }

    [Fact]
    public void FixedColumnsUseTheSamePackingAndScaleOnWidth()
    {
        // 3 columns of 430 in 1000 wide: scale 1000/1290 = 0.7752; tallest (c=3) 1420 * 0.7752 = 1100.8 > 800 -> scrolls.
        FitResult r = LayoutFit.ForColumns(1000, 800, Seven, MinW, 3);
        Assert.Equal(3, r.Columns);
        Assert.Equal(1000.0 / 1290.0, r.Scale, 9);
        Assert.Equal(430, r.ColumnWidth, 9);
        Assert.Equal(1420, r.TallestColumn, 9);
        Assert.Equal(new[] { 0, 1, 2, 2, 0, 1, 2 }, r.ColumnOf);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void ScrollingPlanLeavesRoomForTheScrollbar()
    {
        // As above, but the 20 px scrollbar leaves 980: scale 980/1290.
        FitResult r = LayoutFit.Plan(LayoutMode.Columns3, 1000, 800, Seven, MinW, 20);
        Assert.Equal(980.0 / 1290.0, r.Scale, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void FloorPlanRecomputesForTheScrollbar()
    {
        // 600 - 20 = 580: columns floor(580 / 236.5) = 2, width 580 / 1.1 = 527.27.
        FitResult r = LayoutFit.Plan(LayoutMode.Fit, 600, 400, Seven, MinW, 20);
        Assert.Equal(2, r.Columns);
        Assert.Equal(0.55, r.Scale, 9);
        Assert.Equal(580 / 1.1, r.ColumnWidth, 9);
        Assert.True(r.Scrolls);
    }

    [Fact]
    public void InvalidInputGivesOneUnscaledColumn()
    {
        FitResult r = LayoutFit.ChooseFit(0, 400, Seven, MinW);
        Assert.Equal(1, r.Columns);
        Assert.Equal(1, r.Scale);
    }

    [Fact]
    public void HotkeyCyclesThroughAllLayouts()
    {
        Assert.Equal(LayoutMode.Columns2, LayoutFit.Next(LayoutMode.Fit));
        Assert.Equal(LayoutMode.Auto, LayoutFit.Next(LayoutMode.Columns4));
        Assert.Equal(LayoutMode.Fit, LayoutFit.Next(LayoutMode.Auto));
    }

    [Fact]
    public void EveryModeHasADropdownName()
    {
        foreach (LayoutMode mode in Enum.GetValues<LayoutMode>())
        {
            LayoutOption option = LayoutFit.Options.Single(o => o.Mode == mode);
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.Equal(option.Name, option.ToString());
        }
        Assert.Equal(LayoutFit.Modes, LayoutFit.Options.Select(o => o.Mode).ToArray());
    }
}
