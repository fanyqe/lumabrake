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

## Overlay Does Not Appear

- Confirm the app is enabled.
- Confirm the active content actually crosses spike thresholds.
- Run `monitor list` to confirm Windows reports every active display.
- Protected or exclusive full-screen content may limit capture.

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
