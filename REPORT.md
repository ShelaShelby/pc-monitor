# pc-monitor 0.3 "pack and polish" — report

## Fixed (from Alex's screenshots, 1593x997, Fit, specs on)
1. **Fit wasted space**: cells were 430 px × tallest card. Now cards are packed into columns by their measured
   height (masonry: card order kept, next card into the shortest column, ties leftmost; `LayoutFit.Pack`,
   `Controls/MasonryPanel`). For 1..n columns scale = min(W / (cols × 430), H / tallest column), largest wins
   (ties: fewer columns); columns are stretched to fill the full width (never below 430). 0.55 floor + vertical
   scroll kept; 2/3/4 columns use the same packing. Auto unchanged. Tests: `LayoutFitTests` (7 cards at 1920x1080,
   1024x768, 600x400, ties, fixed columns, scrollbar), numbers worked out in the comments.
2. **Clipped text** (",162", "RPI", label touching value): the value sat in a `*` column squeezed by shared label
   and min/max columns. Rows are now [label Auto, min 90, wraps above 170][gap 14][value Auto, min 64][gap 14]
   [min/max, rest, right-aligned]. Min/max lines may only wrap between "min …" and "max …" (no trimming), and
   show the unit once when both ends share it ("min 1,050 · max 1,199 RPM"). Hero value font 46 → 44.
   Sizes live in `Core/CardMetrics.cs`. New WPF test project `PcMonitor.Ui.Tests` measures every label, value
   and min/max half (fake data + widest values, °C/°F, normal/compact) with Segoe UI at 0.55, 0.68 and 1.0 and
   checks it against its column (widths compared to each other, no fixed pixel values).
3. **Blank Layout dropdown**: the standard white ComboBox showed the app's light text. Dark ComboBox style
   (box, text, arrow, popup, hover, selection) in `Theme.xaml`. The first CI screenshot showed the record dump
   ("LayoutOption { M…") in the closed box (DisplayMemberPath is not used there); now an ItemTemplate and
   `LayoutOption.ToString() => Name`. Tests: SelectedItem text non-empty and equal to its name for each mode.
4. **White title bar**: `DwmSetWindowAttribute` dark mode (20, fallback 19) after the handle exists; errors ignored.
5. **NVMe shown as SCSI**: Interface now uses `MSFT_PhysicalDisk.BusType` (root\Microsoft\Windows\Storage,
   matched by disk number); 17 → "NVMe". The Microsoft docs page was not reachable from the build machine, so
   other BusType values keep what Win32_DiskDrive reports, as does any query failure (`StorageBus`, tests).
6. Version 0.3 (toolbar and title bar).

## Checks
- Linux during work: build of all projects + Core tests. Windows CI: build, Core + UI tests, publish, screenshots.
- CI screenshots (`--fake`, 1024x768 cap, `ci-screenshots` branch): Fit + specs, Fit + Compact,
  2 columns (no PawnIO, specs), Layout dropdown open (`--open-layout-box`).

## Still open / limits
- Not run on the target PC. The real Motherboard card has more fan rows than the fake one; long sensor labels wrap.
- Fit uses card heights from the previous layout pass and settles in up to 3 passes; heights can change slightly
  when columns get wider (less wrapping).
- Clock question from 0.2 (OWNER-TODO 8) still waits for Alex's log. Unchanged: ADL fallback untested on RX 9000,
  generic GPU-edge/board thresholds, unsigned exe.
