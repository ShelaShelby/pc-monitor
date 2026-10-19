# pc-monitor 0.1 — report

## Works
- One dark window, 7 cards (CPU, GPU, RAM, Drive C, Drive D, Motherboard/Fans, Network), reflowing to the window width.
- Per card: two large values with min/max since start and 60 s sparklines, all other values with min/max, specs section (toolbar "Show specs").
- Settings: update rate 0.5/1/2 s, °C/°F, always-on-top, F11 fullscreen; window position/size/monitor remembered.
- PawnIO detection at start (registry + device open), banner with pawnio.eu link and Recheck (reopens LibreHardwareMonitor).
- Sensors via LibreHardwareMonitorLib 0.9.6 (latest stable); GPU values missing there are read from the AMD driver's ADL PMLog.
- Specs from WMI/registry: CPU name, cores/threads, base clock, L3; RAM size/speed/slots/modules; GPU model/VRAM/driver; drive models; board/BIOS.
- Temperature colours: CPU Tjmax 89 °C (AMD), GPU junction 110 °C (AMD), GDDR6 95/105 °C (Micron), DDR5 85/95 °C (JEDEC), NVMe from the drive's own WCTEMP/CCTEMP.
- FakeSensorProvider (`--fake`), unit tests for history, min/max, conversion, thresholds, formatting, n/a, settings.
- CI on windows-latest: build, test, single-file publish, screenshots, release `latest` targeting the head commit.

## n/a and why
- CPU temperature/clocks/power, motherboard fans/temps, DIMM temps: `n/a (needs PawnIO driver)` until PawnIO is installed.
- CPU boost clock in specs: Windows/WMI does not report it; the live "Clock (max core)" shows the highest observed clock.
- GPU memory temperature, hotspot, fan: shown only if the driver exposes them (LibreHardwareMonitor, then ADL); otherwise `n/a` with reason.
- Drive health/hours/temperature: `n/a` if the drive or controller driver does not pass SMART through.

## Known problems / limits
- Not run on the target PC: no Windows here. Real hardware paths are compiled and smoke-tested only on the CI VM (no GPU, no SMART, no PawnIO).
- GPU edge and motherboard thresholds are derived/generic (no vendor limit published) — marked as such in `ThresholdTable`.
- The exe is unsigned (SmartScreen warning).
