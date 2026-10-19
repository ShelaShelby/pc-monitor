# pc-monitor

<img src="assets/logo.png" width="96" alt="PC Monitor logo">

A dark, full-window dashboard for a second monitor that live-reads every part of the PC:
CPU, GPU, RAM, Drive C, Drive D, motherboard/fans and network. Load, temperatures, clocks, power,
fans, VRAM, SMART health, free space, read/write speed. Min/max since start, 60-second sparklines,
green/yellow/red temperature colours from the vendors' published limits.

Built with C# / .NET 8 WPF and [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
(AMD ADL as GPU fallback, WMI for specs). No sign-in, no network access.

## Install
1. Download `pc-monitor-setup.exe` from the **latest** release.
2. Run it (no administrator rights needed to install; Windows SmartScreen warns because it is unsigned:
   "More info" → "Run anyway"). Keep or change the folder (default `%LOCALAPPDATA%\Programs\PC Monitor`),
   leave **Create a desktop shortcut** ticked, finish with **Run PC Monitor** ticked.
3. Start it from the desktop or Start Menu shortcut and accept the administrator prompt (sensors need it).
4. Drag it to the second monitor. F11 = fullscreen, Esc leaves fullscreen. Position, size,
   monitor, update rate (0.5/1/2 s), °C/°F and always-on-top are remembered
   (`%LOCALAPPDATA%\PcMonitor\settings.json`; log in `pc-monitor.log` next to it).

Uninstall: Settings → Apps → Installed apps → PC Monitor → Uninstall (also deletes settings.json and pc-monitor.log).
Portable alternative: download `pc-monitor.exe` from the same release and double-click it (self-contained, no .NET needed).

`pc-monitor.exe --fake` runs on simulated values (FakeSensorProvider), useful without hardware.

## Layouts
Toolbar **Layout** (or key **L** to cycle): **Fit to window** (default; cards packed into columns by their height,
scaled and stretched to fill the window, never below 55 % — then it scrolls), **2 / 3 / 4 columns** (same packing,
scaled to the width, scrolls vertically),
**Auto** (unscaled wrapping). **Compact** hides specs and tightens spacing. Both are remembered.

## PawnIO (CPU temperature)
Ryzen CPU temperatures, clocks and power, and the motherboard fan/temperature chip, need the **PawnIO** driver
(https://pawnio.eu). Without it a banner shows a link and a Recheck button; the rest keeps working. WinRing0 is never used.

## Build
`dotnet test` / `dotnet publish src/PcMonitor.App -c Release -r win-x64` (.NET 8 SDK). Installer: Inno Setup 6, `installer/pc-monitor.iss` (version comes from `Directory.Build.props`). Logo: `python tools/make-icon.py`
(Pillow) writes `assets/logo.png` and `assets/app.ico`; both are committed. CI builds, tests, publishes, builds and tests the
installer and updates the `latest` release (`pc-monitor-setup.exe` + `pc-monitor.exe`) on every push to main.
