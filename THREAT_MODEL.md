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
- Imported profiles are not implemented yet; future imports must validate schema and ranges.

## Open Risks

- Phase 2 GDI capture is not the final DXGI pipeline.
- Overlay capture exclusion depends on Windows behavior.
- HDR normalization is not implemented.
- Protected content detection is not implemented.
- Multi-monitor reconnect and GPU device-loss recovery are future work.
