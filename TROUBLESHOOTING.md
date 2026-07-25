# Troubleshooting

## Emergency Disable

Press `Ctrl+Alt+Shift+L` or choose `Emergency disable` from the tray menu.

To clear CLI-configured manual dimming:

```powershell
dotnet run --project src/LumaBrake.Cli -- dim off
```

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

## Diagnostics Package

```powershell
dotnet run --project src/LumaBrake.Cli -- diagnostics export
```
