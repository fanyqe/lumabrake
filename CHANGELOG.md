# Changelog

## Unreleased

- Initialized the .NET 10 LTS solution.
- Added Phase 2 Brightness Spike Guard vertical slice.
- Added WPF dashboard, tray icon, click-through overlay, emergency hotkey, and CLI settings commands.
- Added deterministic luminance, spike detector, effect composition, and settings recovery tests.
- Added Windows monitor enumeration and `lumabrake monitor list` CLI smoke command.
- Added basic Phase 3 per-monitor capture, detector state, diagnostics, and overlay windows.
- Added tile-aware spike suppression so small bright regions are less likely to trigger full-monitor dimming.
- Added an initial tile-temporal Repetitive Flash Guard and wired it into the global dimming composer.
- Added a CLI diagnostics export package that avoids screenshots and raw screen content.
- Added global manual software dimming in the dashboard and CLI.
- Added a shared Safe Mode policy and `--safe-mode` app startup argument.
- Added initial localized flash masks for small repetitive flashing tile regions.
- Added an initial StaticShade detector for persistent bright tile regions.
