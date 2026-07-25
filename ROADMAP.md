# Roadmap

## 0.1.0 - Phase 2 Vertical Slice

- .NET 10 LTS solution structure.
- Local luminance analysis.
- Brightness Spike Guard detector.
- Click-through software dimming overlay.
- WPF dashboard and tray controls.
- Emergency hotkey.
- Detector and effect tests.

## 0.2.0 - Multi-Monitor

- Display enumeration.
- One overlay per physical monitor.
- Per-monitor settings.
- Initial DPI/orientation/reconnect topology refresh with Windows display-change notification and polling fallback.

## 0.3.0 - Repetitive Flash Guard

- Tile history.
- Local and global flash response.
- Initial red-dominant flash detection.
- Diagnostic panel.

## 0.4.0 - Localized Masking

- Initial tile regional mask composition.
- Initial padded neighboring-region merge.
- Initial feathered region edges.
- Full-screen fallback.

## 0.5.0 - StaticShade

- Initial static bright tile detection.
- Initial exposure scoring.
- Initial OLED, HUD, office, and disabled CLI presets.

## 0.6.0 - Adaptive and Inactive Dimming

- Initial smooth content dimming.
- Initial non-primary inactive monitor dimming.
- Initial tile-activity exceptions for video-like movement.

## 0.7.0 - Hardware Brightness and Schedules

- Read-only DDC/CI capability detection.
- Read-only hardware brightness reading.
- Hardware brightness rate-limit policy and display blacklist.
- Guarded one-shot hardware brightness writes.
- Initial CLI startup schedule restoration.
- Initial runtime scheduled software dimming.
- Initial software profile save/apply CLI and dashboard controls.
- Initial richer schedule CLI editing.

## 0.8.0 - Focus Dimming and Tint

- Basic CLI-controlled tint layer.
- Active-window focus dimming.
- Focus dimming CLI controls.
- Tint schedules and initial per-monitor tint.

## 1.0.0 - Stable Release

- Self-contained portable package and initial WiX MSI installer.
- Multi-monitor SDR support.
- Safe Mode.
- Diagnostics export.
- Initial HDR/advanced-color detection and diagnostics.
- CI release artifacts, checksum, and SBOM.
- Accurate documentation of limitations.
