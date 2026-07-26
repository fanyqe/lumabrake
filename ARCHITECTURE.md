# Architecture

LumaBrake is a native Windows desktop application split into testable domain logic and Windows-specific runtime modules.

## Modules

- `LumaBrake.App`: WPF dashboard, tray icon, app lifetime.
- `LumaBrake.Domain`: immutable records and enums shared by the system.
- `LumaBrake.Analysis`: sRGB linearization, luminance metrics, spike detection.
- `LumaBrake.Effects`: centralized opacity composition and release envelopes.
- `LumaBrake.Capture`: current GDI per-monitor downscaled capture path.
- `LumaBrake.Rendering`: click-through overlay windows, one per active monitor.
- `LumaBrake.MonitorControl`: read-only DDC/CI capability discovery for physical monitors.
- `LumaBrake.Platform.Windows`: Win32 interop for hotkeys, capture, monitor bounds, overlay styles.
- `LumaBrake.Persistence`: local JSON settings with atomic replace.
- `LumaBrake.Diagnostics`: pipeline snapshots for UI and future export.
- `LumaBrake.Cli`: automation entry point for basic settings.

## Phase 4 Data Flow

```text
Each active monitor
  -> startup restoration applies persisted software dimming, tint, or active schedule once
  -> GDI downscaled BGRA capture (160 x 90)
  -> sRGB-to-linear luminance calculation
  -> global average / p95 / bright pixel ratio / tile luminance summary
  -> per-monitor BrightnessSpikeDetector
  -> broad bright-tile transition check
  -> per-monitor RepetitiveFlashDetector
  -> per-monitor StaticShadeDetector
  -> per-monitor AdaptiveContentDimmingDetector
  -> per-monitor InactiveMonitorDimmingDetector for non-primary displays
  -> active schedule request as a low-priority runtime effect
  -> per-monitor EffectEnvelope
  -> EffectComposer with optional tint
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

Consequence: hot-plug event subscriptions and persistent monitor matching are still future work, but CLI, UI, capture, and rendering now use the same monitor source.

## ADR-0004: Basic Per-Monitor Runtime

Decision: maintain one detector/envelope/counter pipeline per active monitor and one WPF overlay window per active monitor.

Reason: sudden brightness changes must be detected and rendered independently for each display before localized masks, inactive-monitor dimming, and per-monitor settings are added.

Consequence: the current GDI backend still captures sequentially on one worker loop. DXGI Desktop Duplication or Windows Graphics Capture remains the target for lower latency and better scaling.

## ADR-0005: Tile-Aware Spike Suppression

Decision: calculate a small tile luminance summary for each analyzed frame and require broad changed bright-tile coverage before a global brightness spike is emitted.

Reason: p95 luminance can be high when a small region is bright, but a small bright object should not always trigger full-monitor dimming.

Consequence: the first Phase 4 slice improves false-positive suppression for global dimming. Localized spike masks and richer scene-change diagnostics remain future work.

## ADR-0006: Repetitive Flash Temporal State

Decision: track per-tile bright/dark transitions inside a bounded time window and emit a `RepetitiveFlash` effect when enough transitions affect enough tiles.

Reason: repetitive flashes are not the same as a single dark-to-bright spike; they require temporal transition counting and cooldown handling.

Consequence: the first Phase 5 slice produced a global dimming response for detected repetitive flashes. Red-flash detection and richer diagnostics remain future work.

## ADR-0007: Initial Localized Flash Masks

Decision: when repetitive flashing affects a small tile area, carry the affected tile regions through `FlashEvent`, `EffectRequest`, `FinalEffectState`, and the renderer instead of converting the event into full-monitor dimming.

Reason: a flashing advertisement, HUD element, or small preview should be dimmed locally without unnecessarily darkening the rest of the monitor.

Consequence: Phase 6 now has a first working tile-mask path using one click-through overlay window per monitor. Broad flash coverage still falls back to global dimming, and feathered edges plus merged region smoothing remain future work.

## ADR-0008: Initial StaticShade Tile State

Decision: track only per-tile luminance averages, bright-pixel ratios, and static duration for StaticShade instead of storing raw frames or screenshots.

Reason: persistent bright UI regions can be detected deterministically from bounded tile metrics while preserving the privacy rule that screen content is not persisted.

Consequence: Phase 7 has an initial localized dimming path for small bright static regions. Motion scoring, accumulated exposure scoring, OLED profiles, and full-screen intent detection remain future work.

## ADR-0009: Initial Adaptive Content Dimming

Decision: emit a low-priority global `AdaptiveContent` effect only after bright scene metrics remain above thresholds for a minimum duration.

Reason: persistent white or bright interfaces benefit from gentle dimming, but short flashes and sudden transitions are already handled by higher-priority flash and spike guards.

Consequence: Phase 8 has a first smooth bright-scene dimming path with duration, dead-zone, smoothing, and frame-gap reset. Inactive-monitor dimming, time-of-day profiles, and application-specific behavior remain future work.

## ADR-0010: Initial Inactive Monitor Dimming

Decision: dim only non-primary monitors when their analyzed luminance metrics stay effectively unchanged for a configured duration.

Reason: unchanged-frame detection is deterministic and available in the existing local analysis loop, while cursor/foreground-window attribution needs a separate Windows activity model.

Consequence: Phase 8 now has a first inactive-monitor response that restores when frame metrics change. Cursor entry, foreground-window ownership, video exceptions, and blackout mode remain future work.

## ADR-0011: Optional Tint Render Layer

Decision: carry tint as part of `FinalEffectState` and render it as a full-monitor overlay layer after analysis is complete.

Reason: tint should never alter the captured frame metrics used by luminance, flash, static, adaptive, or inactivity detectors.

Consequence: Phase 9 has a basic CLI-controlled warm/neutral tint path. Per-monitor tint, schedules, richer color modes, and UI controls remain future work.

## ADR-0012: Read-Only DDC/CI Capability Discovery

Decision: expose hardware brightness support first as read-only `lumabrake hardware list` and `lumabrake hardware brightness` diagnostic commands that enumerate physical monitor handles and read DDC/CI capability and brightness metadata.

Reason: DDC/CI calls can be slow or unreliable on some displays, so hardware control must be introduced separately from the low-latency capture, analysis, and overlay loop.

Consequence: Phase 10 now has a safe capability and brightness-read base plus rate-limit and blacklist policy defaults. Brightness writes, reconnect recovery, and schedule/profile integration remain future work.

## ADR-0013: Startup Schedule Foundation

Decision: model schedules as deterministic local day-mask plus minute-of-day ranges and apply the first active schedule during application startup.

Reason: startup restoration is useful before a full runtime scheduler exists, and local minute ranges avoid storing absolute timestamps or timezone-specific secrets in settings.

Consequence: Phase 11 has a first CLI path for night schedules and startup restoration. Runtime refresh, per-monitor schedules, profile assignment, DST edge policy, and hardware brightness targets remain future work.

## ADR-0014: Runtime Scheduled Dimming

Decision: evaluate active schedules inside the controller loop and pass them to the effect composer as transient low-priority requests instead of writing them into the effect envelope.

Reason: schedule dimming is ambient baseline behavior and should not outrank manual overrides, flash protection, spike protection, or adaptive content decisions.

Consequence: schedules now affect running overlays without double-applying manual dimming. Per-monitor schedules, explicit tint-off overrides, richer editors, and deterministic clock injection remain future work.
