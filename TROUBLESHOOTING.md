# Troubleshooting

## Emergency Disable

Press `Ctrl+Alt+Shift+L` or choose `Emergency disable` from the tray menu.

To launch with overlays disabled and all manual dimming values cleared:

```powershell
dotnet run --project src/LumaBrake.App -- --safe-mode
```

To clear CLI-configured all-monitor manual dimming:

```powershell
dotnet run --project src/LumaBrake.Cli -- dim off
```

Use `dim monitor remove <monitor-id|device-name>` for a monitor override. Safe Mode also clears monitor manual dimming overrides and disables persisted hardware brightness write permission. Read-only hardware brightness diagnostics remain available from the CLI.

## Windows Startup

Use the dashboard Windows startup controls, or register, inspect, or remove the current-user login startup entry from the published portable package folder:

```powershell
dotnet run --project src/LumaBrake.Cli -- startup status
dotnet run --project src/LumaBrake.Cli -- startup enable
dotnet run --project src/LumaBrake.Cli -- startup disable
```

The startup entry runs `LumaBrake.App.exe --background`, which starts the controller and tray icon without opening the dashboard. `startup-restore` only controls restored dimming, schedule state, and the stored hardware-brightness target after the app starts; it does not register Windows login startup.

## Duplicate Launches

LumaBrake keeps one desktop instance per Windows session. Starting the app again signals the existing process to show the dashboard, then exits before it creates another tray icon or overlay set.

## MSI Installer

The MSI installs the self-contained app and CLI payload for the current user. It includes a default Start menu shortcut and optional Desktop shortcut. Windows startup remains a current-user setting; enable it from the dashboard or `LumaBrake.Cli.exe startup enable` after install.

Uninstall removes installed files and installer-created shortcuts. Local settings under the current user's application data folder are preserved by default.

To validate the MSI payload without installing it:

```powershell
.\scripts\test-installer-extract.ps1 -InstallerPath .\artifacts\LumaBrake-win-x64-setup.msi -ExtractRoot .\artifacts\msi-extract
```

To validate silent install and uninstall in a disposable test directory:

```powershell
.\scripts\test-installer-install.ps1 -InstallerPath .\artifacts\LumaBrake-win-x64-setup.msi -InstallRoot "$env:TEMP\lumabrake-msi-install" -LogRoot .\artifacts\msi-logs
```

## Overlay Does Not Appear

- Confirm the app is enabled.
- Confirm the active content actually crosses spike thresholds.
- Run `monitor list` to confirm Windows reports every active display.
- Protected or exclusive full-screen content may limit capture. When sampling keeps returning near-empty frames, LumaBrake pauses automatic effects for that monitor and keeps manual dimming, schedules, and tint available.

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
dotnet run --project src/LumaBrake.Cli -- capture-test all
```

`capture-test` checks the primary monitor. `capture-test all` enumerates every active Windows monitor, captures the same `160 x 90` analysis sample from each display, analyzes luminance, reports sampling quality, and prints capture plus analysis elapsed time per monitor.

## Diagnostics Export

Use `Export diagnostics` from the dashboard or tray menu. The package is written under the current user's Documents folder by default.

CLI export is also available:

```powershell
dotnet run --project src/LumaBrake.Cli -- diagnostics export
```

Diagnostics packages include settings, monitor descriptors, runtime pipeline counters, active effect reasons, capture anomaly status, and HDR/advanced-color monitor state when available. Filesystem paths are redacted; screenshots, raw frames, recordings, OCR output, and screen content are not included.

## Monitor List

```powershell
dotnet run --project src/LumaBrake.Cli -- monitor list
```

The running dashboard refreshes the monitor topology periodically. Use `Refresh monitors` if you need an immediate manual refresh after changing displays.

`monitor list` also reports `advancedColor`. If it shows `active`, Windows has HDR or another advanced-color mode enabled for that display. LumaBrake suppresses automatic effects on that monitor while keeping manual dimming, schedules, and tint available.

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

Inactive-monitor dimming also uses the foreground window and cursor position as local interaction signals, so a dimmed non-primary monitor should clear promptly when the active window moves there or the cursor enters it.

## Profiles

Profiles save reusable visual settings for reuse, including the startup-restoration hardware-brightness target:

```powershell
dotnet run --project src/LumaBrake.Cli -- profile save evening Evening Work
dotnet run --project src/LumaBrake.Cli -- profile list
dotnet run --project src/LumaBrake.Cli -- profile apply evening
```

The dashboard Profiles panel can also save the current settings, apply a saved profile immediately, or remove a saved profile. CLI profile apply updates persisted settings; use the dashboard controls for a running WPF session when you want software changes to take effect without restarting the app. Stored hardware-brightness targets do not perform a runtime DDC write by themselves.

## Monitor Manual Dimming

Use `monitor list` to copy a device name or stable id, then set a monitor-specific manual dimming value:

```powershell
dotnet run --project src/LumaBrake.Cli -- monitor list
dotnet run --project src/LumaBrake.Cli -- dim monitor set "\\.\DISPLAY2" 35
dotnet run --project src/LumaBrake.Cli -- dim monitor list
dotnet run --project src/LumaBrake.Cli -- dim monitor set "\\.\DISPLAY2" off
dotnet run --project src/LumaBrake.Cli -- dim monitor remove "\\.\DISPLAY2"
```

The dashboard Monitor dimming control can also set the selected monitor value for a running WPF session. `dim monitor set ... off` keeps a 0% override for that monitor; `dim monitor remove` makes the monitor use the all-monitor manual dimming value again.

## Application Rules

Application rules pause automatic effects for matching foreground apps while keeping manual dimming, schedules, and tint active:

```powershell
dotnet run --project src/LumaBrake.Cli -- app-rule add video --process vlc --title-contains Video
dotnet run --project src/LumaBrake.Cli -- app-rule list
dotnet run --project src/LumaBrake.Cli -- app-rule disable-entry video
dotnet run --project src/LumaBrake.Cli -- app-rule enable-entry video
dotnet run --project src/LumaBrake.Cli -- app-rule remove video
```

Use `app-rule disable` to pause all application rules without deleting them.

The dashboard Application Rules panel can also save, remove, clear, enable, or pause foreground application rules for a running WPF session.

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
dotnet run --project src/LumaBrake.Cli -- schedule hardware work "\\.\DISPLAY2" 0 70 --fallback-software
dotnet run --project src/LumaBrake.Cli -- schedule disable-entry work
dotnet run --project src/LumaBrake.Cli -- schedule enable-entry work
dotnet run --project src/LumaBrake.Cli -- schedule remove work
dotnet run --project src/LumaBrake.Cli -- schedule list
```

The days value accepts `everyday`, `weekdays`, `weekend`, or comma-separated day names such as `mon,wed,fri`.

Schedule hardware targets identify the display device, physical monitor index, requested brightness, and optional software fallback. They are evaluated for startup restoration; the runtime overlay loop still avoids continuous DDC writes.

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

Hardware writes are disabled by default. A real CLI set write requires `hardware writes enable`, a single display device, a physical monitor index from the diagnostic output, rate-limit clearance, no blacklist match, and `--confirm-hardware-write`.

If the hardware write cannot complete, use an explicit software fallback:

```powershell
dotnet run --project src/LumaBrake.Cli -- hardware brightness set "\\.\DISPLAY2" 0 70 --confirm-hardware-write --fallback-software
```

The fallback stores a monitor manual dimming override for that display. Use `dim monitor remove "\\.\DISPLAY2"` to clear it.

To store a startup-restoration hardware target without writing to the monitor immediately:

```powershell
dotnet run --project src/LumaBrake.Cli -- startup-restore hardware "\\.\DISPLAY2" 0 70 --fallback-software
dotnet run --project src/LumaBrake.Cli -- startup-restore status
```

On app launch, the effective startup hardware target is attempted once only when hardware writes are enabled. If the write fails and the target includes `--fallback-software`, LumaBrake stores the matching software dimming fallback instead.

## Diagnostics Package

```powershell
dotnet run --project src/LumaBrake.Cli -- diagnostics export
```
