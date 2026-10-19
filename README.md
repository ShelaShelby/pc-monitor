# pc-monitor

A dark, full-window dashboard for a second monitor that live-reads every part of the PC:
CPU, GPU, RAM, Drive C, Drive D, motherboard/fans and network. Load, temperatures, clocks, power,
fans, VRAM, SMART health, free space, read/write speed. Min/max since start, 60-second sparklines,
green/yellow/red temperature colours from the vendors' published limits.

Built with C# / .NET 8 WPF and [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
(AMD ADL as GPU fallback, WMI for specs). No sign-in, no network access.

## Run
1. Download `pc-monitor.exe` from the **latest** release (self-contained, no .NET install needed).
2. Double-click it and accept the administrator prompt (sensors need it).
3. Drag it to the second monitor. F11 = fullscreen, Esc leaves fullscreen. Position, size,
   monitor, update rate (0.5/1/2 s), °C/°F and always-on-top are remembered
   (`%LOCALAPPDATA%\PcMonitor\settings.json`; log in `pc-monitor.log` next to it).

`pc-monitor.exe --fake` runs on simulated values (FakeSensorProvider), useful without hardware.

## Layouts
Toolbar **Layout** (or key **L** to cycle): **Fit to window** (default; all cards scaled to fit, no scrolling,
never below 55 % — then it scrolls), **2 / 3 / 4 columns** (scaled to the width, scrolls vertically),
**Auto** (unscaled wrapping). **Compact** hides specs and tightens spacing. Both are remembered.

## PawnIO (CPU temperature)
Ryzen CPU temperatures, clocks and power, and the motherboard fan/temperature chip, need the **PawnIO** driver
(https://pawnio.eu). Without it a banner shows a link and a Recheck button; the rest keeps working. WinRing0 is never used.

## Build
`dotnet test` / `dotnet publish src/PcMonitor.App -c Release -r win-x64` (.NET 8 SDK). CI builds, tests, publishes and updates the `latest` release on every push to main.
