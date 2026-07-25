# Contributing

LumaBrake favors small, tested vertical slices.

- Keep deterministic analysis code separate from Windows interop.
- Add synthetic frame tests for detector behavior.
- Do not add AI, cloud screen analysis, OCR, screen recording, DLL injection, or DRM bypass features.
- Avoid persisting raw frame data.
- Document new Windows API choices in `ARCHITECTURE.md` as an ADR.
