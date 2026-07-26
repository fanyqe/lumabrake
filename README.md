# LumaBrake

LumaBrake is an open-source Windows utility that analyzes screen luminance in real time and reduces sudden brightness spikes, repetitive flashes, static bright regions, and unnecessary brightness on inactive displays.

The current working slices cover Brightness Spike Guard, an initial Repetitive Flash Guard, tile-based localized flash masks, StaticShade, adaptive content dimming, initial inactive-monitor dimming, optional tint, DDC/CI hardware brightness diagnostics with guarded one-shot writes, and initial runtime schedules:

- processing stays local;
- raw screen samples are discarded after analysis;
- no AI, cloud service, telemetry, OCR, or screen recording is used;
- the app runs from a native WPF dashboard and system tray;
- click-through overlays apply software dimming on active Windows monitors;
- active Windows monitors and DDC/CI hardware capabilities can be listed from the CLI;
- hardware brightness writes are disabled by default and require settings opt-in, a physical monitor index, rate-limit clearance, and per-command confirmation;
- `Ctrl+Alt+Shift+L` performs emergency disable;
- corrupted local settings are quarantined and replaced with defaults;
- protected content, HDR, exclusive full-screen apps, and GPU/display driver behavior may limit operation.

LumaBrake is not a medical device and does not guarantee seizure prevention or OLED burn-in prevention.

## Current Features

| Feature | Status |
| --- | --- |
| Brightness Spike Guard | Phase 4 tile-aware spike suppression in progress |
| Multi-monitor capture and overlays | Basic Phase 3 pipeline implemented |
| Manual software dimming | Basic global dimming implemented |
| System tray controls | Implemented |
| Emergency hotkey | Implemented |
| Local deterministic luminance analysis | Implemented |
| Repetitive Flash Guard | Initial tile temporal detector implemented |
| Localized Flash Masking | Initial tile regional overlay implemented |
| StaticShade | Initial static bright tile masking implemented |
| Adaptive Content Dimming | Initial persistent bright-scene dimming implemented |
| Inactive Monitor Dimming | Initial non-primary unchanged-frame dimming implemented |
| Focus Dimming | Roadmap |
| Optional Tint | Basic CLI-controlled tint implemented |
| Hardware Brightness | Read-only DDC/CI diagnostics and guarded one-shot CLI writes implemented |
| Profiles, rules, schedules | Initial CLI schedule, startup restoration, and runtime scheduled dimming implemented |

## Build

Install .NET 10 LTS SDK, then run:

```powershell
dotnet restore
dotnet build LumaBrake.sln
dotnet test LumaBrake.sln --no-build
```

The local development machine can also use the user-local SDK installed at `%USERPROFILE%\.dotnet`.

## Run

```powershell
dotnet run --project src/LumaBrake.App
dotnet run --project src/LumaBrake.App -- --safe-mode
```

The CLI currently manages startup settings:

```powershell
dotnet run --project src/LumaBrake.Cli -- status
dotnet run --project src/LumaBrake.Cli -- disable
dotnet run --project src/LumaBrake.Cli -- safe-mode
dotnet run --project src/LumaBrake.Cli -- dim 25
dotnet run --project src/LumaBrake.Cli -- tint warm 10
dotnet run --project src/LumaBrake.Cli -- tint off
dotnet run --project src/LumaBrake.Cli -- capture-test
dotnet run --project src/LumaBrake.Cli -- monitor list
dotnet run --project src/LumaBrake.Cli -- hardware list
dotnet run --project src/LumaBrake.Cli -- hardware brightness
dotnet run --project src/LumaBrake.Cli -- hardware brightness plan "\\.\DISPLAY2" 0 70
dotnet run --project src/LumaBrake.Cli -- hardware writes status
dotnet run --project src/LumaBrake.Cli -- hardware blacklist add "\\.\DISPLAY2"
dotnet run --project src/LumaBrake.Cli -- schedule night 22:00 07:00 24 warm 18
dotnet run --project src/LumaBrake.Cli -- schedule list
dotnet run --project src/LumaBrake.Cli -- startup-restore status
dotnet run --project src/LumaBrake.Cli -- diagnostics export
```

## Safety

The overlay is click-through, non-activating, hidden from the taskbar, and asks Windows to exclude it from capture where supported. If capture or analysis fails, the controller clears active dimming before retrying.

Hardware brightness writes are not used by the runtime overlay loop. The CLI write path is a one-shot administrative command: it is disabled by default, targets one display device plus one physical monitor index, honors the blacklist and durable write interval, and also requires `--confirm-hardware-write` on each write command.
