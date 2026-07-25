# Architecture

LumaBrake is a native Windows desktop application split into testable domain logic and Windows-specific runtime modules.

## Modules

- `LumaBrake.App`: WPF dashboard, tray icon, app lifetime.
- `LumaBrake.Domain`: immutable records and enums shared by the system.
- `LumaBrake.Analysis`: sRGB linearization, luminance metrics, spike detection.
- `LumaBrake.Effects`: centralized opacity composition and release envelopes.
- `LumaBrake.Capture`: current Phase 2 GDI primary-monitor capture path.
- `LumaBrake.Rendering`: click-through overlay window.
- `LumaBrake.Platform.Windows`: Win32 interop for hotkeys, capture, monitor bounds, overlay styles.
- `LumaBrake.Persistence`: local JSON settings with atomic replace.
- `LumaBrake.Diagnostics`: pipeline snapshots for UI and future export.
- `LumaBrake.Cli`: automation entry point for basic settings.

## Phase 2 Data Flow

```text
Primary monitor
  -> GDI downscaled BGRA capture (160 x 90)
  -> sRGB-to-linear luminance calculation
  -> global average / p95 / bright pixel ratio
  -> BrightnessSpikeDetector
  -> EffectEnvelope
  -> EffectComposer
  -> click-through WPF overlay opacity
```

Raw frame buffers are scoped to one capture iteration and are not persisted.

## ADR-0001: Phase 2 Capture

Decision: use a simple GDI `StretchBlt` downscaled primary-monitor capture path for the first vertical slice.

Reason: it gives a working Windows-native path with low implementation risk and deterministic testable analysis while DXGI Desktop Duplication and Windows Graphics Capture are designed.

Consequence: this is not the final high-performance capture backend. DXGI Desktop Duplication remains the preferred production path for multi-monitor and low-latency work.

## ADR-0002: Overlay Safety

Decision: create one top-level transparent WPF overlay for the primary monitor in Phase 2, set it to click-through and non-activating, and call `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` where supported.

Consequence: Windows versions or capture paths that do not honor display affinity may still require a future feedback-loop mitigation path.
