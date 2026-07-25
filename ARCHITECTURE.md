# Architecture

LumaBrake is a native Windows desktop application split into testable domain logic and Windows-specific runtime modules.

## Modules

- `LumaBrake.App`: WPF dashboard, tray icon, app lifetime.
- `LumaBrake.Domain`: immutable records and enums shared by the system.
- `LumaBrake.Domain.Color`: deterministic Night Color temperature, schedule, pause, and app-exception evaluation.
- `LumaBrake.Analysis`: sRGB linearization, luminance metrics, spike detection.
- `LumaBrake.Effects`: centralized opacity composition and release envelopes.
- `LumaBrake.Capture`: current GDI per-monitor downscaled capture path.
- `LumaBrake.Rendering`: click-through overlay windows, one per active monitor.
- `LumaBrake.MonitorControl`: DDC/CI capability, brightness read, and guarded one-shot brightness write interop for physical monitors.
- `LumaBrake.Platform.Windows`: Win32 interop for hotkeys, capture, monitor bounds, foreground-window and cursor monitor ownership, and overlay styles.
- `LumaBrake.Persistence`: local JSON settings, software profiles, and atomic replace.
- `LumaBrake.Diagnostics`: pipeline snapshots for UI and future export.
- `LumaBrake.Cli`: automation entry point for basic settings.

## Phase 4 Data Flow

```text
Each active monitor
  -> startup restoration applies persisted software dimming, tint, profile-applied settings, or active schedule once
  -> periodic monitor topology refresh adds, updates, or removes per-monitor pipelines
  -> GDI downscaled BGRA capture (160 x 90)
  -> sRGB-to-linear luminance calculation
  -> global average / p95 / bright pixel ratio / tile luminance and red-dominance summary
  -> per-monitor BrightnessSpikeDetector
  -> broad bright-tile transition check
  -> per-monitor RepetitiveFlashDetector
  -> per-monitor StaticShadeDetector
  -> per-monitor AdaptiveContentDimmingDetector
  -> per-monitor InactiveMonitorDimmingDetector for non-primary displays
  -> foreground and cursor monitor lookup for optional focus dimming and inactive-monitor restore
  -> active schedule request as a low-priority runtime effect
  -> Night Color request from deterministic schedule/foreground-app rules, independent of captured frame metrics
  -> per-monitor diagnostics counters, including red-dominant flash events
  -> per-monitor EffectEnvelope
  -> EffectComposer with Night Color, scheduled, per-monitor, or global tint
  -> matching click-through WPF overlay global opacity / regional masks / tint
```

Raw frame buffers are scoped to one capture iteration and are not persisted.

## ADR-0001: Phase 2 Capture

Decision: use a simple GDI `StretchBlt` downscaled primary-monitor capture path for the first vertical slice.

Reason: it gives a working Windows-native path with low implementation risk and deterministic testable analysis while DXGI Desktop Duplication and Windows Graphics Capture are designed.

Consequence: this is not the final high-performance capture backend. DXGI Desktop Duplication remains the preferred production path for multi-monitor and low-latency work.

## ADR-0002: Overlay Safety

Decision: create one top-level transparent WPF overlay for the primary monitor in Phase 2, set it to click-through and non-activating, and call `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` where supported.

Consequence: Windows versions or capture paths that do not honor display affinity may still require a future feedback-loop mitigation path.

## ADR-0003: Monitor Enumeration

Decision: Phase 3 begins with Win32 `EnumDisplayMonitors` and `GetMonitorInfo` enumeration feeding a shared domain monitor descriptor.

Reason: the product needs stable monitor identity, bounds, work area, and primary/secondary state before per-monitor capture sessions and overlays are expanded.

Consequence: Windows display-change events and runtime topology polling can add, update, or remove per-monitor pipelines while the app is running. Persistent monitor matching is still future work, but CLI, UI, capture, and rendering now use the same monitor source.

## ADR-0004: Basic Per-Monitor Runtime

Decision: maintain one detector/envelope/counter pipeline per active monitor and one WPF overlay window per active monitor.

Reason: sudden brightness changes must be detected and rendered independently for each display before localized masks, inactive-monitor dimming, and per-monitor settings are added.

Consequence: the current GDI backend still captures sequentially on one worker loop. DXGI Desktop Duplication or Windows Graphics Capture remains the target for lower latency and better scaling.

## ADR-0005: Tile-Aware Spike Suppression

Decision: calculate a small tile luminance summary for each analyzed frame and require broad changed bright-tile coverage before a global brightness spike is emitted.

Reason: p95 luminance can be high when a small region is bright, but a small bright object should not always trigger full-monitor dimming.

Consequence: the first Phase 4 slice improves false-positive suppression for global dimming. Localized spike masks and richer scene-change diagnostics remain future work.

## ADR-0006: Repetitive Flash Temporal State

Decision: track per-tile bright/dark transitions inside a bounded time window and emit a `RepetitiveFlash` effect when enough transitions affect enough tiles. Each tile also carries a red-dominance signal so repeated red-black flashes can trigger even when relative luminance stays below the normal bright-tile threshold.

Reason: repetitive flashes are not the same as a single dark-to-bright spike; they require temporal transition counting and cooldown handling.

Consequence: the first Phase 5 slices produce global or localized dimming for detected repetitive flashes, including red-dominant flashes. Richer diagnostics remain future work.

## ADR-0007: Initial Localized Flash Masks

Decision: when repetitive flashing affects a small tile area, carry the affected tile regions through `FlashEvent`, `EffectRequest`, `FinalEffectState`, and the renderer instead of converting the event into full-monitor dimming.

Reason: a flashing advertisement, HUD element, or small preview should be dimmed locally without unnecessarily darkening the rest of the monitor.

Consequence: Phase 6 now has a padded, merged, and feathered tile-mask path using one click-through overlay window per monitor. Broad flash coverage still falls back to global dimming, and GPU-oriented mask rendering remains future work.

## ADR-0008: Initial StaticShade Tile State

Decision: track only per-tile luminance averages, 95th percentile luminance, bright-pixel ratios, exposure score, and static duration for StaticShade instead of storing raw frames or screenshots.

Reason: persistent bright UI regions can be detected deterministically from bounded tile metrics while preserving the privacy rule that screen content is not persisted. OLED-oriented presets tune the same bounded metrics without introducing screenshots, OCR, app inspection, or remote screen processing.

Consequence: Phase 7 has an exposure-scored localized dimming path for small bright static regions plus first CLI-applied StaticShade presets. Motion scoring, accumulated cross-session exposure memory, and full-screen intent detection remain future work.

## ADR-0009: Initial Adaptive Content Dimming

Decision: emit a low-priority global `AdaptiveContent` effect only after bright scene metrics remain above thresholds for a minimum duration.

Reason: persistent white or bright interfaces benefit from gentle dimming, but short flashes and sudden transitions are already handled by higher-priority flash and spike guards.

Consequence: Phase 8 has a first smooth bright-scene dimming path with duration, dead-zone, smoothing, and frame-gap reset. Inactive-monitor dimming, time-of-day profiles, and application-specific behavior remain future work.

## ADR-0010: Initial Inactive Monitor Dimming

Decision: dim only non-primary monitors when their analyzed luminance metrics stay effectively unchanged for a configured duration.

Reason: unchanged-frame detection is deterministic and available in the existing local analysis loop. Tile activity catches video-like motion where global luminance stays stable, while foreground-window and cursor monitor attribution restore the inactive monitor promptly when the user interacts with it.

Consequence: Phase 8 now has a first inactive-monitor response that restores when frame metrics or enough tile regions change, when foreground activity moves onto the monitor, or when the cursor enters it. Richer input timing, app-specific activity exceptions, and blackout mode remain future work.

## ADR-0011: Optional Tint Render Layer

Decision: carry tint as part of `FinalEffectState` and render it as a full-monitor overlay layer after analysis is complete.

Reason: tint should never alter the captured frame metrics used by luminance, flash, static, adaptive, or inactivity detectors.

Consequence: Phase 9 has a basic CLI-controlled warm/neutral tint path. Schedules, richer color modes, and UI controls remain future work.

## ADR-0012: Read-Only DDC/CI Capability Discovery

Decision: expose hardware brightness support first as read-only `lumabrake hardware list` and `lumabrake hardware brightness` diagnostic commands that enumerate physical monitor handles and read DDC/CI capability and brightness metadata.

Reason: DDC/CI calls can be slow or unreliable on some displays, so hardware control must be introduced separately from the low-latency capture, analysis, and overlay loop.

Consequence: Phase 10 now has a safe capability and brightness-read base plus rate-limit and blacklist policy defaults. Schedule/profile hardware-brightness targets can be persisted, the effective startup target can be applied once, and a low-frequency runtime coordinator applies changed targets outside the overlay frame loop. Reconnect recovery and richer per-monitor hardware target mapping remain future work.

## ADR-0013: Startup Schedule Foundation

Decision: model schedules as deterministic local day-mask plus minute-of-day ranges and apply the first active schedule during application startup.

Reason: startup restoration is useful before a full runtime scheduler exists, and local minute ranges avoid storing absolute timestamps or timezone-specific secrets in settings.

Consequence: Phase 11 has a first CLI path for night schedules, startup restoration, and persisted hardware-brightness targets. Runtime hardware writes, per-monitor schedule matching beyond target identity, profile assignment, and DST edge policy remain future work.

## ADR-0014: Runtime Scheduled Dimming

Decision: evaluate active schedules inside the controller loop and pass them to the effect composer as transient low-priority requests instead of writing them into the effect envelope.

Reason: schedule dimming is ambient baseline behavior and should not outrank manual overrides, flash protection, spike protection, or adaptive content decisions.

Consequence: schedules now affect running overlays without double-applying manual dimming. Per-monitor schedules, explicit tint-off overrides, and deterministic clock injection remain future work.

## ADR-0015: Guarded Hardware Brightness Writes

Decision: expose hardware brightness writes only as a one-shot CLI path that targets one display device plus one physical monitor index and requires persisted write permission, blacklist clearance, durable write-interval clearance, and `--confirm-hardware-write`.

Reason: DDC/CI writes can alter real monitor state and can behave inconsistently across displays. The runtime overlay loop should remain software-only until hardware behavior has broader recovery, timeout, and profile handling.

Consequence: Phase 12 has a safe write gate, state-backed rate limiting, versioned schedule/profile hardware targets, and one-shot startup target application without automatic runtime hardware dimming. Future work still needs reconnect recovery, maximum-step policy, and richer monitor identity matching before hardware writes are used continuously by the runtime.

## ADR-0016: Initial Focus Dimming

Decision: resolve the monitor that owns the current foreground window once per controller tick, then add a low-priority `FocusDimming` request only to other known monitors when the user enables focus dimming.

Reason: foreground-window ownership is a local Windows signal and can dim unused monitors without inspecting screen content or persisting raw frames. The effect must stay below spike, flash, static, inactive, and adaptive protection so safety responses remain the reported active reason.

Consequence: Phase 13 has default-disabled focus dimming with CLI controls and testable provider injection. Future work still needs richer app/window rules and per-monitor profiles.

## ADR-0017: Initial Software Profiles

Decision: store named profiles as persisted software settings snapshots that include maximum software opacity, manual dimming, per-monitor manual dimming, tint, Night Color, focus dimming, and explicit hardware-brightness targets only.

Reason: profiles should make day-to-day overlay behavior repeatable without changing global enablement, schedules, startup restoration, or physical monitor brightness.

Consequence: Phase 14 has CLI save, list, apply, remove, import, and export commands plus dashboard save, apply, and remove controls for software profiles. Applying a profile updates persisted settings; richer editors remain future work.

## ADR-0017c: Bounded Profile File Transfer

Decision: import and export individual profile JSON files through a dedicated persistence helper instead of importing whole settings files.

Reason: profile sharing is useful, but untrusted full-settings import would touch more runtime behavior than a user likely intended. Keeping transfer scoped to one `LumaBrakeProfile`, requiring a bounded JSON object with a non-empty id, and clamping before save prevents extreme values from entering persisted settings.

Consequence: CLI profile import/export can upsert one clamped profile without executing commands, reading screen content, or changing unrelated settings. Whole-settings import, profile bundles, and richer schema compatibility reporting remain future work.

## ADR-0017a: Foreground Application Rules

Decision: introduce deterministic foreground application rules that match process names and optional window-title fragments, then suppress automatic effects while a rule matches the active foreground app.

Reason: per-application exclusions are useful for apps where automatic luminance responses may be distracting, but manual dimming and scheduled comfort settings should remain under user control. The first slice uses foreground app state instead of screenshots or OCR.

Consequence: application rules now have runtime scopes for automatic dimming modules and Night Color plus dashboard and CLI management commands. Richer window identity matching remains future work.

## ADR-0017b: Transient Runtime Loop Recovery

Decision: treat capture, analysis, and overlay loop exceptions as transient runtime failures by clearing active effects and retrying instead of invoking the user-facing emergency-disable path.

Reason: display topology changes, capture API failures, and render surface hiccups can happen on Windows without representing a user intent to turn LumaBrake off. The fail-safe requirement is to remove dimming immediately, then let the next healthy frame recover.

Consequence: emergency disable still turns the runtime off and clears manual dimming. Transient loop failures keep persisted/runtime settings intact while active overlays are hidden on a best-effort basis before the retry delay, and snapshot/dashboard status exposes recovery count and last recovery time. Recoverable per-monitor capture failures clear only the affected monitor and keep healthy monitors rendering, while the overlay renderer recreates a monitor's WPF overlay window after recoverable update, hide, or dispose failures before reapplying a clear or requested effect. Broader device-specific capture/render recovery policies remain future work.

## ADR-0018: Editable Schedule CLI

Decision: add general schedule set, remove, enable-entry, and disable-entry commands on top of the existing night shortcut while preserving the current schedule JSON shape.

Reason: the model already supports multiple named entries, priority by list order, per-entry enabled state, day masks, dimming, and tint. Editing those entries through the CLI is useful without changing runtime schedule evaluation.

Consequence: Phase 15 has basic named schedule editing with parsed day names and stable upsert/remove helpers. GUI schedule editing, drag-to-reorder priority, per-monitor schedules, explicit tint-off overrides, and clock-injected runtime boundary tests remain future work.

## ADR-0019: Initial Per-Monitor Tint

Decision: store per-monitor tint overrides keyed by monitor stable id or display device name and resolve them per controller pipeline before composing the monitor's final effect.

Reason: the renderer already receives one `FinalEffectState` per monitor, so per-monitor tint can be added without changing overlay rendering, capture, detector math, or the effect composer contract.

Consequence: Phase 16 has CLI-managed per-monitor tint overrides. Active schedule tint keeps priority over monitor overrides to preserve existing schedule behavior; if no schedule tint is active, monitor tint overrides global tint, including an explicit `off` override. Global `tint off` does not clear monitor overrides. GUI tint controls, monitor override clear-all, and richer monitor identity recovery remain future work.

## ADR-0020: Periodic Monitor Topology Refresh

Decision: refresh the monitor catalog on a low-frequency controller interval and update pipelines plus overlay windows only when the topology changes.

Reason: monitor reconnects, disconnects, resolution changes, orientation changes, and DPI changes should not require an application restart, but refreshing every frame would add unnecessary work and UI churn.

Consequence: running sessions can recover from basic monitor add/remove/update events through both Windows display-change notifications and periodic polling. When an existing monitor descriptor changes, automatic detector state and stale overlay effects are reset before the new geometry is used. Recoverable monitor-enumeration failures clear the active monitor list and overlays while surfacing a failed-detection status instead of crashing startup or the refresh loop. Richer reconnect identity matching and hardware-brightness reconnect recovery remain future work.

## ADR-0021: Current-User Startup Registration

Decision: expose Windows login startup as explicit dashboard controls and a CLI command that write the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry and launch `LumaBrake.App.exe --background`.

Reason: login startup is useful before a full installer exists, but it should remain user-controlled, per-user, and removable without administrator privileges. Starting in background keeps sign-in quiet while preserving the tray and emergency-disable paths.

Consequence: Phase 17 has a first portable-package startup path with testable registry abstraction and dashboard controls. Installer integration, app identity, and signed binaries remain future work.

## ADR-0022: HDR and Advanced-Color Awareness

Decision: read active Windows display paths with `QueryDisplayConfig`, attach `DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO` metadata to each monitor descriptor, and suppress automatic luminance-derived effects on monitors where advanced color is active.

Reason: HDR and wide-color modes affect luminance interpretation, but the current capture and analysis path is still SDR-oriented. Reporting advanced-color state through the monitor list, dashboard, and diagnostics makes this limitation visible; suppressing automatic effects avoids applying SDR thresholds to HDR content before calibrated HDR normalization exists.

Consequence: Phase 18 can identify monitors where HDR/advanced color is active, handle lookup failures as an explicit unknown state, and keep manual dimming, schedules, and tint available on HDR monitors. Automatic spike, flash, StaticShade, adaptive, inactive-monitor, and focus dimming effects remain intentionally disabled on active advanced-color monitors until HDR normalization, separate HDR sensitivity values, and protected-content behavior are implemented.

## ADR-0023: Single Desktop Instance

Decision: acquire a local named mutex during WPF startup before creating overlays, global hotkeys, or the tray icon. The primary instance also listens on a local named activation event.

Reason: LumaBrake runs system-wide visual overlays. Two desktop instances in the same Windows session could create duplicated dimming layers, conflicting tray state, and confusing emergency-disable behavior.

Consequence: a second launch signals the existing process to show the dashboard, then exits early. Duplicate runtime effects are prevented without introducing a background service.

## ADR-0024: Near-Empty Capture Handling

Decision: classify sustained effectively empty zeroed frame buffers as limited sampling, suppress automatic luminance-derived effects, and reset detector state for that monitor. Manual dimming, schedules, tint, and Night Color still compose normally.

Reason: protected, exclusive full-screen, or driver-limited content can make capture APIs return empty frames even when the monitor is active. Treating those frames as real black content can poison spike detector baselines and create false reactions when capture becomes available again.

Consequence: the runtime and `capture-test` report limited sampling without persisting screen content. Legitimate fully black content with a real pixel buffer remains available to flash detection. Hard recoverable capture failures are tracked separately from near-empty limited sampling so diagnostics can distinguish failed frame acquisition from protected-content-like empty samples. Future DXGI or Windows Graphics Capture work should add stronger protected-content and device-loss signals beyond zeroed-buffer heuristics.

## ADR-0025: Per-Monitor Manual Software Dimming

Decision: store per-monitor manual dimming overrides keyed by monitor stable id or display device name and resolve them per controller pipeline before schedule dimming and effect composition.

Reason: global manual dimming is useful as an all-monitor fallback, but multi-monitor setups often need one display dimmed differently or explicitly left undimmed. The renderer already receives one final effect per monitor, so this can be added without changing capture, detector, or overlay contracts.

Consequence: the dashboard and CLI can set, list, and remove monitor-specific manual dimming. An explicit `off` command stores a 0% override, while remove restores inheritance from the all-monitor value. Profiles capture the overrides, and emergency disable, Safe Mode, and disabled startup restoration clear them together with global manual dimming. Per-monitor schedules and monitor identity migration remain future work.

## ADR-0026: Versioned Settings Schema

Decision: store `schemaVersion` in `settings.json` and normalize loaded or saved settings through a single `SettingsMigration.CurrentSchemaVersion` gate.

Reason: user settings are now broad enough that future field renames, defaults, and one-time backfills need an explicit version boundary instead of ad hoc JSON shape checks.

Consequence: Phase 19 has a schema-version field and migration entry point. Version 2 adds hardware-brightness target fields for schedules, startup restoration, and profiles while old JSON files continue to load through default property backfill. Future migrations should be added to this policy before renaming or removing persisted setting contracts.

## ADR-0027: Explicit Hardware Brightness Software Fallback

Decision: keep hardware brightness writes as explicit CLI-only commands and add `--fallback-software` as a separate opt-in path that stores a monitor manual dimming override when a hardware write fails.

Reason: DDC/CI writes can be slow, unsupported, blocked, rate-limited, or unavailable on many monitors. Falling back to software dimming is safer than retrying hardware writes automatically, but it should still be visible and user-controlled because it changes persisted dimming settings.

Consequence: failed one-shot, startup, or scheduled runtime hardware writes can produce a deterministic software fallback without touching the runtime frame loop. The fallback uses the requested hardware brightness percentage to derive monitor manual dimming opacity. Schedule/profile hardware targets are persisted separately; reconnect recovery and richer per-monitor hardware target mapping remain future work.

## ADR-0029: Low-Frequency Runtime Hardware Brightness Coordination

Decision: apply changed schedule/profile hardware-brightness targets from a low-frequency app-level coordinator instead of from the capture, analysis, or overlay rendering loop.

Reason: DDC/CI writes are too slow and unreliable for frame-by-frame protection, but scheduled and profile-driven hardware brightness changes are acceptable when they are explicit, opt-in, rate-limited, and isolated from software overlay responsiveness.

Consequence: runtime hardware brightness now follows active schedule targets before the stored startup/profile target when hardware writes are enabled and allowed. The coordinator seeds the launch target so startup restoration is not repeated, records durable native write attempts, and applies opt-in software fallback only when a target requests it.

## ADR-0028: Hardware Brightness Target Contract

Decision: store hardware-brightness targets as explicit schedule, startup-restoration, and profile fields containing display device name, physical monitor index, requested brightness, and optional software fallback.

Reason: hardware brightness integration needs a durable, versioned contract before any runtime DDC write path can safely consume user intent. Keeping the target separate from global hardware write permission prevents a profile or schedule from silently enabling real monitor writes.

Consequence: users can prepare and inspect hardware targets through the CLI and profiles can carry the startup-restoration hardware target. The app can apply the effective startup target once and can apply changed schedule/profile targets through the low-frequency coordinator using the existing hardware write gate and optional software fallback. The runtime overlay loop still avoids continuous DDC writes.

## ADR-0030: Night Color Comfort Layer

Decision: model Night Color as a default-off deterministic comfort layer with manual Kelvin, custom local minute schedule, approximate sunrise/sunset schedule, editable location, brightness, color strength, transition smoothing, pause/resume, full-screen pause, and app-specific exclusions. The controller evaluates it outside the screen-content detectors. On Windows it first applies the effect through display gamma ramps; if gamma ramp control is unavailable or unsafe for the active monitor set, it falls back to the existing overlay tint/dimming path.

Reason: time-of-day color warmth is user intent, not a luminance-analysis conclusion. It should not depend on captured pixels, and it should never feed back into spike, flash, StaticShade, adaptive, or inactivity detection.

Consequence: Night Color feels closer to native utilities such as LightBulb because SDR displays receive real gamma-ramp color-temperature and brightness multipliers. Gamma writes are throttled to avoid stutter and the original ramp is restored on clear, emergency disable, shutdown, or fallback. Overlay fallback keeps Night Color usable when Windows, HDR/protected display state, or a display driver refuses gamma ramp changes.
