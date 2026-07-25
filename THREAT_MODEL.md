# Threat Model

## Protected Assets

- User screen content.
- Local settings.
- Emergency disable availability.
- Input pass-through behavior.
- Release artifacts.

## Current Mitigations

- Raw frame buffers are not persisted.
- Overlay windows are click-through and non-activating.
- Emergency disable clears active effects immediately.
- Settings are written through a temporary file and atomic replace.
- Settings and hardware-brightness state use unique temporary files and non-overwriting corrupt-file quarantine to reduce configuration-corruption risk.
- CI release artifacts include SHA-256 checksums.
- Release workflow runs only from existing annotated `vX.Y.Z` tags and checks out the tagged commit before creating a verified-tag GitHub Release.
- CI verifies release artifact checksums, required portable ZIP contents, and CycloneDX SBOM JSON before upload.
- CI extracts the portable ZIP and runs the packaged self-contained CLI before upload.
- MSI extraction smoke tests verify that app, CLI, and runtime files are present before upload.
- MSI install/uninstall smoke tests verify silent install, CLI startup status, and app executable removal before upload.
- Release packaging supports optional Authenticode signing for portable EXE/DLL payloads and the MSI before checksum generation when trusted certificate secrets are configured.
- Release artifact verification requires valid Authenticode signatures for signed release builds when signing certificate secrets are configured.
- Diagnostics export redacts filesystem paths and avoids screenshots, raw frames, recordings, OCR output, and screen content.
- HDR/advanced-color display state is reported from Windows metadata without capturing or storing screen content.
- Active HDR/advanced-color monitors use conservative handling that suppresses automatic luminance-derived effects while preserving manual dimming, schedules, and tint.
- Near-empty capture handling uses an in-memory zeroed-buffer check only; raw frames are not persisted while automatic detector state is protected from empty samples.
- Recoverable per-monitor capture failures clear only the affected monitor and are reported separately from near-empty limited sampling.
- Application rules match local foreground process/window metadata only; they do not inspect, persist, or classify screen contents.
- Recoverable overlay update, hide, and dispose failures recreate the affected overlay window before effects continue.
- Recoverable monitor-enumeration failures clear active monitor pipelines and overlays while reporting failed detection instead of leaving stale effects active.
- Imported profile files must be a bounded JSON object with a non-empty id; imported values are clamped before they are saved.

## Open Risks

- Phase 2 GDI capture is not the final DXGI pipeline.
- Overlay capture exclusion depends on Windows behavior.
- HDR normalization is not implemented; automatic protective effects on active HDR/advanced-color monitors remain intentionally disabled until calibrated HDR handling exists.
- Protected content detection is heuristic; near-empty sampling is handled conservatively, but Windows may still block or distort capture in ways that require a future capture backend.
- Multi-monitor reconnect has event-triggered and polling recovery for basic topology changes, and recoverable monitor-enumeration failures fail closed with empty monitor state. Recoverable per-monitor GDI capture/device-loss failures and WPF overlay surface-loss failures are isolated from healthy monitors, while hardware-specific GPU reset evidence still requires physical-device smoke testing.
- Release artifacts remain unsigned if trusted signing certificate secrets are not configured, and SmartScreen reputation still requires real distribution history.
- Broader manual install and uninstall coverage across Windows editions, locked-down policy environments, and SmartScreen behavior is still recommended before stable public release.
- Whole-settings import is not implemented; only individual software profile JSON import/export is supported.
