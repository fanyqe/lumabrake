# Troubleshooting

## Emergency Disable

Press `Ctrl+Alt+Shift+L` or choose `Emergency disable` from the tray menu.

To launch with overlays disabled and manual dimming cleared:

```powershell
dotnet run --project src/LumaBrake.App -- --safe-mode
```

To clear CLI-configured manual dimming:

```powershell
dotnet run --project src/LumaBrake.Cli -- dim off
```

Safe Mode also disables persisted hardware brightness write permission. Read-only hardware brightness diagnostics remain available from the CLI.

## Windows Startup

Register, inspect, or remove the current-user login startup entry from the published portable package folder:

```powershell
dotnet run --project src/LumaBrake.Cli -- startup status
dotnet run --project src/LumaBrake.Cli -- startup enable
dotnet run --project src/LumaBrake.Cli -- startup disable
```

The startup entry runs `LumaBrake.App.exe --background`, which starts the controller and tray icon without opening the dashboard. `startup-restore` only controls restored dimming and schedule state after the app starts; it does not register Windows login startup.

## Overlay Does Not Appear

- Confirm the app is enabled.
- Confirm the active content actually crosses spike thresholds.
- Run `monitor list` to confirm Windows reports every active display.
- Protected or exclusive full-screen content may limit capture.

## Repetitive Flash Guard

The flash detector uses tile-level luminance transitions and red-dominance transitions. Red-dominant detection can be disabled or tuned in `flashGuard` settings with `redFlashDetectionEnabled`, `redDominanceThreshold`, and `redDominantPixelRatioThreshold`.

The dashboard keeps the main protection-event total and appends a red-flash count when red-dominant events have been detected.

Localized masks are padded, merged, and feathered before rendering. If a flashing region covers too much of the frame, LumaBrake still falls back to global dimming instead of drawing many regional masks.

## StaticShade Presets

StaticShade detects persistent bright regions with bounded tile metrics and exposure scoring. Inspect or apply presets from the CLI:

```powershell
dotnet run --project src/LumaBrake.Cli -- static-shade status
dotnet run --project src/LumaBrake.Cli -- static-shade list
dotnet run --project src/LumaBrake.Cli -- static-shade preset oled-conservative
```

Use `oled-aggressive` for earlier OLED-focused masking, `static-hud` for small persistent HUDs, `office` for gentler bright-document work, and `disabled` to turn StaticShade off.

## Build Uses The Wrong SDK

Run:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet --list-sdks
```

## Capture Smoke Test

```powershell
dotnet run --project src/LumaBrake.Cli -- capture-test
```

## Monitor List

```powershell
dotnet run --project src/LumaBrake.Cli -- monitor list
```

The running dashboard refreshes the monitor topology periodically. Use `Refresh monitors` if you need an immediate manual refresh after changing displays.

## Focus Dimming

Focus dimming is disabled by default. Enable it and adjust opacity from the CLI:

```powershell
dotnet run --project src/LumaBrake.Cli -- focus enable
dotnet run --project src/LumaBrake.Cli -- focus opacity 22
```

If it does not change any monitor, confirm multiple monitors are detected and that the foreground app is on one of those monitors:

```powershell
dotnet run --project src/LumaBrake.Cli -- status
dotnet run --project src/LumaBrake.Cli -- monitor list
```

## Profiles

Profiles save software dimming settings for reuse:

```powershell
dotnet run --project src/LumaBrake.Cli -- profile save evening Evening Work
dotnet run --project src/LumaBrake.Cli -- profile list
dotnet run --project src/LumaBrake.Cli -- profile apply evening
```

Profile apply updates persisted settings. Restart the dashboard if it is already running and you need the WPF app to load those persisted changes.

## Monitor Tint

Use `monitor list` to copy a device name or stable id, then set a monitor-specific tint:

```powershell
dotnet run --project src/LumaBrake.Cli -- monitor list
dotnet run --project src/LumaBrake.Cli -- tint monitor set "\\.\DISPLAY2" neutral 18
dotnet run --project src/LumaBrake.Cli -- tint monitor list
dotnet run --project src/LumaBrake.Cli -- tint monitor remove "\\.\DISPLAY2"
```

An active schedule tint takes priority over monitor tint. Otherwise monitor tint overrides the global tint for the matching display. `tint off` clears only the global tint; use `tint monitor remove` for monitor overrides.

## Schedules

Create, pause, resume, or remove named schedules from the CLI:

```powershell
dotnet run --project src/LumaBrake.Cli -- schedule set work weekdays 09:00 17:00 18 neutral 8 Work Hours
dotnet run --project src/LumaBrake.Cli -- schedule disable-entry work
dotnet run --project src/LumaBrake.Cli -- schedule enable-entry work
dotnet run --project src/LumaBrake.Cli -- schedule remove work
dotnet run --project src/LumaBrake.Cli -- schedule list
```

The days value accepts `everyday`, `weekdays`, `weekend`, or comma-separated day names such as `mon,wed,fri`.

## Hardware Brightness

Read DDC/CI capability and brightness diagnostics:

```powershell
dotnet run --project src/LumaBrake.Cli -- hardware list
dotnet run --project src/LumaBrake.Cli -- hardware brightness
```

Plan a one-shot hardware write without changing the monitor:

```powershell
dotnet run --project src/LumaBrake.Cli -- hardware brightness plan "\\.\DISPLAY2" 0 70
```

Hardware writes are disabled by default. A real write requires `hardware writes enable`, a single display device, a physical monitor index from the diagnostic output, rate-limit clearance, no blacklist match, and `--confirm-hardware-write`.

## Diagnostics Package

```powershell
dotnet run --project src/LumaBrake.Cli -- diagnostics export
```
