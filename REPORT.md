# pc-monitor 0.5 — audit report

Full read of every source file, robustness tests with fake data, a 3-minute soak test on the Windows CI.

| # | Found (file) | Status |
|---|---|---|
| 1 | Exit closed the LHM driver while a sensor read could still run on the thread pool (MainWindow.OnClosing, MainViewModel) | Fixed: exit waits up to 5 s for the read, then disposes |
| 2 | An exception while showing an update ended the update loop for good, freezing every card (MainViewModel.RunAsync) | Fixed: per-update and per-card try/catch, logged |
| 3 | A part that disappears (drive removed, adapter gone) kept showing its last values and a frozen sparkline (CardViewModel, MetricTracker) | Fixed: n/a + "No data"; missing sensors are recorded as gaps (test) |
| 4 | Two sensors with the same name shared one history and one min/max (LhmSensorProvider keys) | Fixed: SnapshotGuard makes keys unique (tests) |
| 5 | GPU choice preferred any AMD GPU, so an AMD iGPU beat an Nvidia card, and an iGPU won if the RX had no memory sensor (PickGpu) | Fixed: GpuPicker, discrete before integrated (tests) |
| 6 | RAM card ran a WMI query on every update (2×/s at 0.5 s); board name and VRAM registry scan too (ReadRam, ReadBoard, ReadGpu) | Fixed: read once, cached |
| 7 | No single-instance guard: a second start opened the drivers again and both wrote settings.json | Fixed: named mutex, second start focuses the first and exits (CI step) |
| 8 | No recovery after sleep/resume | Fixed: LHM is reopened on resume (PowerModeChanged) — not testable on CI |
| 9 | Closing while minimised forgot "maximised" (MainWindow.OnClosing) | Fixed: last non-minimised state saved |
| 10 | Log timestamp used the user's culture time separator (Log.Write) | Fixed: invariant |
| 11 | Network: an adapter chosen at start stayed selected after its cable was unplugged / VPN went down | Fixed: re-pick when Windows reports it down and another is up (checked every 20 reads) |
| 12 | WMI failure while mapping C:/D: at start aborted the whole app (LhmSensorProvider.Open) | Fixed: logged, card falls back to DriveInfo |
| 13 | °C near double.MaxValue showed "Infinity °F" (Formatter) | Fixed: n/a (fuzz test) |
| 14 | LayoutFit cast a possibly out-of-range double to int (machine-dependent) | Fixed: clamp before cast (property test) |
| 15 | Uninstall left settings.json.tmp in %LOCALAPPDATA%\PcMonitor | Fixed + CI check |
| 16 | Drive letters are mapped to disks once at start; a disk added/re-lettered later shows SMART of the old mapping until restart | Not fixed: LHM's storage list is also fixed at start; needs a reopen, rare |
| 17 | Installing over a *running* elevated app: the per-user installer cannot close it (CloseApplications) | Not fixed: needs an AppMutex readable across integrity levels; close the app before upgrading |
| 18 | Settings load/save, truncated/corrupt JSON, out-of-range values, atomic write | Checked OK, 30 new tests, no change needed |
| 19 | History buffers capped (60 s, 2000 points), log capped at 2 MB, update loop never overlaps (awaited, gate) | Checked OK |

## Tests added (Core, run under xUnit)
- RobustnessTests: 9 000 hostile snapshots (NaN, ±Inf, ±1e308, negative, missing/duplicate parts and keys, flapping); min/max checked against an independent reference, no NaN/Infinity text, sparkline range finite; missing/returning sensors; reset min/max; °C/°F min/max consistency; GPU choice.
- SettingsCorruptionTests: truncation at every byte, wrong types, deep nesting, enum numbers out of range, odd window bounds.
- CultureTests: formatting and settings under en-US, et-EE, de-DE, fr-FR, tr-TR (hand-written expected strings, Turkish i).
- LayoutFitPropertyTests: 36 000 random plans: never throws, Fit scale ≥ 0.55 (fixed 2/3/4 columns scale to the width by design), columns never wider than the window, packing consistent, deterministic.
- CI: soak test, second-instance test, installer upgrade 0.4 → 0.5 (one Apps entry, one shortcut each, settings kept), exe version = 0.5.

## Soak test (Windows CI, --fake, 100 ms updates, layout/unit/compact/specs/reset cycling every 2.5 s)
Sampled every 10 s after a 30 s warm-up (16 samples); first 3 vs last 3 averages; passed, exit code 0, no errors in the log.

| metric | first 3 | last 3 | change |
|---|---|---|---|
| working set MB | 160.4 | 161.4 | +0.6 % |
| private bytes MB | 78.0 | 89.7 | +15.0 % (borderline: not monotonic, peaked 90.7 at 170 s; GC heap settling, below the 25 % limit) |
| handles | 475.7 | 470.0 | −1.2 % |
| GDI objects | 23.0 | 23.0 | 0 % |
| USER objects | 29.3 | 29.0 | −1.1 % |

## Not checkable from the cloud
Real hardware (Ryzen 7500X3D, RX 9060 XT, B650 SuperIO, PawnIO), sleep/resume recovery, moving between monitors with different DPI,
unplugging the second monitor, two real GPUs, VPN/cable changes, running non-elevated. These paths are covered only by code review and the Core tests.
