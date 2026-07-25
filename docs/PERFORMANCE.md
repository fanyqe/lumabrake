# Performance Notes

Phase 5 analyzes a downscaled `160 x 90` BGRA sample per active monitor, derives a `32 x 18` tile luminance summary plus bright/red signal bounds from the same sample, and keeps bounded per-tile transition queues for repetitive flash detection. Localized spike detection also compares per-tile bright-pixel ratio jumps, which keeps small bright UI flashes responsive without making full-screen dimming more aggressive.

The runtime protection loop starts with an immediate first analysis pass, then samples on a 50 ms cadence. Active monitors are captured and analyzed concurrently, and overlay updates are coalesced per monitor so stale effect states do not queue behind newer protection decisions. Focus Dimming is composed independently enough to remain stable during short near-empty capture anomalies. Dashboard live-monitor snapshots are coalesced separately at a lower visual refresh rate so the UI cannot become the pacing point for overlay protection.

Use the CLI smoke test before release builds and when changing display topology code:

```powershell
dotnet run --project src/LumaBrake.Cli -- capture-test all
```

The command enumerates active Windows monitors, captures and analyzes one SDR-oriented `160 x 90` sample per monitor, then prints source bounds, sample size, luminance metrics, and capture plus analysis elapsed time. Passing output for every monitor listed by `monitor list` is the current multi-monitor SDR acceptance check.

For packaged release candidates, run the field smoke script against the portable package:

```powershell
scripts\test-field-smoke.ps1 -RequireMultipleMonitors
```

This produces `artifacts\field-smoke-evidence.json` with monitor count, capture availability, DDC/CI read-only capability data, diagnostics export validation, startup smoke status, and Authenticode status. It never performs a hardware brightness write.

CI also runs deterministic synthetic performance smoke tests:

```powershell
dotnet test tests\LumaBrake.Performance.Tests\LumaBrake.Performance.Tests.csproj --configuration Release
```

Those tests do not replace hardware profiling. They run the `160 x 90` analyzer, automatic detectors, regional-mask composition, and effect composer over generated video-like frames, then fail on major CPU, allocation, or retained-state regressions that would make long sessions unsafe.

Current CI smoke budgets are intentionally broad to avoid treating hosted-runner noise as a product failure:

- 240 analyzed/composed synthetic frames must finish within 2 seconds in Release builds;
- allocated bytes per analyzed/composed frame must stay below 2.5 MB;
- a 1,800-frame synthetic long run must not retain more than 8 MiB.

GPU usage, real capture FPS, and real multi-monitor render timing remain manual profiling fields until the DXGI/WGC capture and lower-level renderer backends are in place.

Tracked baseline fields:

- capture interval;
- analysis duration;
- capture plus analysis elapsed time from `capture-test all`;
- overlay update duration;
- allocation rate;
- dropped frames;
- CPU and GPU usage on one, two, and three monitors.
