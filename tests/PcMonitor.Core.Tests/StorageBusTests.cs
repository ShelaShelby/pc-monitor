using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class StorageBusTests
{
    [Theory]
    [InlineData(17, "SCSI", "NVMe")]
    [InlineData(17, null, "NVMe")]
    [InlineData(11, "IDE", "IDE")]
    [InlineData(1, "SCSI", "SCSI")]
    [InlineData(null, "SCSI", "SCSI")]
    [InlineData(null, null, null)]
    public void NvmeBusTypeOverridesWin32Interface(int? busType, string? win32, string? expected) =>
        Assert.Equal(expected, StorageBus.InterfaceName(busType, win32));
}
