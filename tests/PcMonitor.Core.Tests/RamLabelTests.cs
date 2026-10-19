using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class RamLabelTests
{
    [Fact]
    public void SameLocatorTwiceGetsNumberAndBank()
    {
        IReadOnlyList<string> labels = RamLabels.For(new[]
        {
            new MemoryStick("DIMM 1", "P0 CHANNEL A"),
            new MemoryStick("DIMM 1", "P0 CHANNEL B"),
        });
        Assert.Equal(new[] { "Stick 1: DIMM 1 / P0 CHANNEL A", "Stick 2: DIMM 1 / P0 CHANNEL B" }, labels);
    }

    [Fact]
    public void IdenticalSticksStayUniqueByOrder()
    {
        IReadOnlyList<string> labels = RamLabels.For(new[] { new MemoryStick("DIMM 1", "BANK 0"), new MemoryStick("DIMM 1", "BANK 0") });
        Assert.Equal(new[] { "Stick 1: DIMM 1 / BANK 0", "Stick 2: DIMM 1 / BANK 0" }, labels);
        Assert.Equal(2, labels.Distinct().Count());
    }

    [Fact]
    public void MissingPartsAreLeftOut()
    {
        IReadOnlyList<string> labels = RamLabels.For(new[] { new MemoryStick(" DIMM_A2 ", null), new MemoryStick(null, "") });
        Assert.Equal(new[] { "Stick 1: DIMM_A2", "Stick 2" }, labels);
    }
}
