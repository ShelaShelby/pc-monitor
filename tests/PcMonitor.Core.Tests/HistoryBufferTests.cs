using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class HistoryBufferTests
{
    [Fact]
    public void KeepsOnlyLastWindow()
    {
        var b = new HistoryBuffer(60);
        for (int t = 0; t <= 120; t++)
            b.Add(t, t);
        var points = b.Snapshot();
        Assert.Equal(60.0, points[0].TimeSeconds);
        Assert.Equal(120.0, points[^1].TimeSeconds);
        Assert.Equal(61, points.Count);
    }

    [Fact]
    public void WindowIsTimeBasedNotCountBased()
    {
        var b = new HistoryBuffer(60);
        for (int i = 0; i <= 240; i++)
            b.Add(i * 0.5, i);
        Assert.Equal(121, b.Count); // 60 s at 0.5 s, both ends inclusive
    }

    [Fact]
    public void MissingValuesAreStoredAsGaps()
    {
        var b = new HistoryBuffer(60);
        b.Add(0, 10);
        b.Add(1, null);
        b.Add(2, double.NaN);
        b.Add(3, 12);
        var p = b.Snapshot();
        Assert.Equal(4, p.Count);
        Assert.Null(p[1].Value);
        Assert.Null(p[2].Value);
        Assert.Equal(12, p[3].Value);
    }

    [Fact]
    public void ClockGoingBackwardsStartsFresh()
    {
        var b = new HistoryBuffer(60);
        b.Add(100, 1);
        b.Add(101, 2);
        b.Add(5, 3);
        var p = b.Snapshot();
        Assert.Single(p);
        Assert.Equal(3, p[0].Value);
    }

    [Fact]
    public void MaxPointsCapsMemory()
    {
        var b = new HistoryBuffer(1000, maxPoints: 10);
        for (int i = 0; i < 50; i++)
            b.Add(i, i);
        Assert.Equal(10, b.Count);
        Assert.Equal(40, b.Snapshot()[0].Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void RejectsInvalidWindow(double window) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoryBuffer(window));

    [Fact]
    public void RejectsInvalidTime()
    {
        var b = new HistoryBuffer();
        Assert.Throws<ArgumentOutOfRangeException>(() => b.Add(double.NaN, 1));
    }
}
