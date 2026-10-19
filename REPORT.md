# pc-monitor 0.2 — report

## Fixed (from Alex's screenshot)
1. **0 readings at start**: LibreHardwareMonitor gives 0 for Ryzen temperature, clocks and package power before the first
   sample. CPU temperature (Tctl/Tdie, CCDs), clocks and package power of exactly 0 are now `n/a (waiting for first sample)`,
   so min/max and sparklines never see them. GPU clocks/power: same, only while the GPU temperature is missing too.
   Fan 0 RPM and 0 % load stay real. Logic in `Core/SensorGuards.cs`, tests in `SensorGuardTests`.
2. **Clock (avg) = Clock (max core)**: cause not proven (no access to the PC). Now: avg = mean of `Core #n (Effective)` if
   present, else `Cores (Average)`, else mean of `Core #n`; max = highest `Core #n`. Every CPU clock sensor with its value,
   and the chosen sources, are written once to `pc-monitor.log` at the first non-zero read. Equal values are still both shown.
3. **"DIMM 1" twice**: WMI query adds `BankLabel`; sticks are labelled `Stick n: <DeviceLocator> / <BankLabel>`
   (`Core/RamLabels.cs`, `RamLabelTests`).
4. **Sparklines exaggerating**: auto range grows around the data midpoint to at least 10 °C / 10 % / 500 MHz / 10 W /
   300 RPM / 1 GB / 1 MB/s (`Core/SparkRange.cs`, `SparkRangeTests`).

## New: layouts
- Toolbar "Layout" dropdown + hotkey L: Fit to window (default), 2/3/4 columns, Auto (0.1 wrapping). "Compact" checkbox
  hides specs, tightens row/card spacing and lowers the sparklines. Both saved in settings.json.
- Fit: `LayoutFit.ChooseFit` tries 1..n columns of uniform cells (430 px wide, height of the tallest card) and takes the
  largest scale (ties: fewer columns); applied as a LayoutTransform. Below 0.55 it stays at 0.55, uses the columns that
  fit the width and scrolls vertically. Fixed columns scale to the width only and scroll vertically if needed (no 0.55
  floor there, so all chosen columns stay visible). Upscaling above 1 is allowed when the window is large.
- Tests: `LayoutFitTests` (1920x1080 7 cards, tall, tiny → 0.55 floor, 1 card, fixed columns, scrollbar, L cycling),
  expected numbers worked out by hand in the test comments.

## Checks
- `dotnet build` + `dotnet test` (Core tests) on Linux during work; full build/test/publish on the Windows CI.
- CI screenshots (capped at the runner's 1024x768 desktop), all with `--fake`: Fit, Fit + Compact,
  and no-PawnIO + specs in 2 columns. They are on the `ci-screenshots` branch.

## Still open / limits
- Not run on the target PC. The clock question (bug 2) needs the new log lines from Alex (OWNER-TODO 8).
- The Layout dropdown uses the standard Windows ComboBox look (not dark-themed).
- At 1024x768 Fit gives ~0.6 scale (4 columns × 2 rows); text is small but everything is visible.
- Unchanged from 0.1: ADL fallback untested on an RX 9000 driver, generic GPU-edge/board thresholds, unsigned exe.
