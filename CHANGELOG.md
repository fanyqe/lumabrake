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
- Added initial adaptive content dimming for persistent bright scenes.
- Added initial inactive-monitor dimming for unchanged non-primary displays.
- Added a basic CLI-controlled tint overlay.
- Added read-only DDC/CI hardware capability detection through the CLI.
- Added read-only hardware brightness readings through the CLI.
- Added hardware brightness policy defaults and display-device blacklist commands.
- Added initial CLI schedules and startup restoration for software dimming and tint.
- Added runtime scheduled dimming as a low-priority effect.
- Added guarded one-shot hardware brightness write commands with opt-in settings, per-command confirmation, physical monitor targeting, blacklist checks, safe-mode clearing, and durable write interval state.
- Added default-disabled focus dimming that uses the foreground window's monitor to dim other monitors at low priority.
- Added CLI software profiles for saving, listing, applying, and removing reusable dimming, tint, and focus settings.
- Added editable CLI schedules with named set, remove, enable-entry, disable-entry, and readable day-mask output.
- Added initial per-monitor tint overrides through the CLI with schedule tint precedence.
- Added red-dominant repetitive flash detection using tile-level red-dominance metrics.
- Added red-dominant flash counters to pipeline diagnostics and the dashboard event display.
