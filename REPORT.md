# pc-monitor 0.4 "logo and installer" — report

## Done
1. **Logo** (original design): dark rounded square in the card colour (#181B22), open 270° gauge ring
   (gap at the bottom) in the app's green (#3DDC84), pulse line across the centre in the accent blue (#4EA1FF).
   Drawn by `tools/make-icon.py` (Python 3 + Pillow, each size drawn separately at 16× and box-filtered).
   Committed outputs: `assets/logo.png` (512 px), `assets/app.ico` (16, 20, 24, 32, 40, 48, 64, 128, 256;
   256 as PNG, the rest 32-bit BMP). ≤ 24 px: no border, thicker ring, no pulse line; 32–48 px: thicker pulse.
   CI never regenerates them. Checked visually at 16, 32 and 256 px (one look).
2. **Icon use**: exe (`ApplicationIcon`), window title bar/taskbar (`Window.Icon`, embedded resource),
   installer and uninstaller (`SetupIconFile`), Apps and Features (`UninstallDisplayIcon` = exe), shortcuts (exe icon).
3. **Installer** `installer/pc-monitor.iss` (Inno Setup 6), built on the Windows CI after publish. ISCC.exe is
   looked up first, installed with choco only if missing (3 attempts, then the step is skipped).
   Per-user (`PrivilegesRequired=lowest`), default `%LOCALAPPDATA%\Programs\PC Monitor`, changeable.
   Pages: Welcome, folder, Tasks ("Create a desktop shortcut", ticked), Finish ("Run PC Monitor").
   Start Menu shortcut always. AppVersion is passed by CI from `Directory.Build.props` (single source; the
   script refuses to compile without it). Publisher ShelaShelby. Uninstall deletes `settings.json` and
   `pc-monitor.log` next to the exe and also in `%LOCALAPPDATA%\PcMonitor` (where the app actually writes
   them), then removes those folders if empty. Exe manifest unchanged (still asks for admin); "Run PC Monitor"
   uses ShellExecute so the UAC prompt can appear. Output `pc-monitor-setup.exe`, attached to `latest`
   next to the portable `pc-monitor.exe`.
4. **CI installer test** (never launches the app): silent install with `/TASKS=desktopicon` into a temp
   folder → exe, desktop .lnk, Start Menu .lnk exist; silent uninstall → exe, uninstaller, settings.json,
   pc-monitor.log and both shortcuts gone. Second install with `/TASKS=""` → no desktop .lnk, Start Menu
   .lnk present; uninstalled again.
5. Version 0.4 (toolbar, title bar, installer). README: install steps. OWNER-TODO: unsigned → SmartScreen.

## Checks
- Linux: icon script run and inspected; workflow YAML parsed. The .NET SDK could not be installed on the
  dev machine this time, so the C# build ran only on the Windows CI (build, Core + UI tests, publish,
  screenshots, installer build + test, release).

## Still open / limits
- Installer and exe are unsigned (SmartScreen warns). Not run on the target PC.
- Unchanged from 0.3: ADL fallback untested on RX 9000, clock question (OWNER-TODO 8), generic thresholds.
