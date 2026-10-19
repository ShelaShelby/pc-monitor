# Things Alex has to do by hand

1. **Install PawnIO** (needed for CPU temperature/clocks/power, motherboard fans and temperatures,
   and RAM DIMM temperatures): go to https://pawnio.eu, download the installer, run it, reboot
   if it asks. Then press **Recheck** in the app's banner (or restart the app).
2. **Install**: GitHub → this repo → Releases → `latest` → download `pc-monitor-setup.exe`, run it, keep
   "Create a desktop shortcut" ticked. (Portable: `pc-monitor.exe` from the same release.)
   **Neither the installer nor the exe is code-signed**, so Windows SmartScreen (and the browser download)
   warns: "More info" → "Run anyway". Removing the warning needs a code-signing certificate (owner decision/cost).
3. **Accept the UAC prompt** each start (the app needs administrator rights for sensors).
   Optional autostart: Task Scheduler → Create Task → "Run with highest privileges",
   trigger "At log on", action = path to `pc-monitor.exe` (avoids the prompt).
4. **Keep the AMD Adrenalin driver installed** — GPU values come from it (ADL fallback uses `atiadlxx.dll`).
5. **Place the window** on the second monitor once; it reopens there. F11 for fullscreen.
6. **Check the first real run**: if a value says `n/a`, the reason is printed next to it. Send
   `%LOCALAPPDATA%\PcMonitor\pc-monitor.log` if something looks wrong; it lists every sensor
   source LibreHardwareMonitor found.
7. If Drive D is not your second NVMe, the card maps by drive letter: give the second drive the letter D:
   in Disk Management, or tell the developer which letter to use.
8. If "Clock (avg)" / "Clock (max core)" still look wrong in 0.2, send `%LOCALAPPDATA%\PcMonitor\pc-monitor.log` (it lists every CPU clock sensor).
