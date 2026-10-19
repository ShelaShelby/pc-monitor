namespace PcMonitor.Core;

/// <summary>
/// All hardware access goes through this interface so the UI can run on simulated data
/// (<see cref="FakeSensorProvider"/>) or on the real machine.
/// Implementations are called from one background thread at a time.
/// </summary>
public interface ISensorProvider : IDisposable
{
    string Name { get; }

    /// <summary>State of the PawnIO driver that CPU temperatures (and SuperIO/SMBus) need.</summary>
    DriverStatus PawnIo { get; }

    void Open();

    /// <summary>Re-detect drivers and, if something changed, reinitialise hardware access.</summary>
    void Recheck();

    /// <summary>Close and reopen hardware access (after sleep/resume, when drivers may have reset).</summary>
    void Reopen();

    /// <summary>Poll every sensor once and return the current values.</summary>
    SystemSnapshot Read();

    /// <summary>Static specs per part. May be called after <see cref="Open"/>.</summary>
    IReadOnlyList<PartSpecs> GetSpecs();
}
