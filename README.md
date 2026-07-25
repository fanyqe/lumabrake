# LumaBrake

LumaBrake is an open-source Windows utility that analyzes screen luminance in real time and reduces sudden brightness spikes, repetitive flashes, static bright regions, and unnecessary brightness on inactive displays.

The first working slice is focused on Brightness Spike Guard:

- processing stays local;
- raw screen samples are discarded after analysis;
- no AI, cloud service, telemetry, OCR, or screen recording is used;
- the app runs from a native WPF dashboard and system tray;
- a click-through overlay applies software dimming;
- `Ctrl+Alt+Shift+L` performs emergency disable;
- corrupted local settings are quarantined and replaced with defaults;
- protected content, HDR, exclusive full-screen apps, and GPU/display driver behavior may limit operation.

LumaBrake is not a medical device and does not guarantee seizure prevention or OLED burn-in prevention.

## Current Features

| Feature | Status |
| --- | --- |
| Brightness Spike Guard | Phase 2 vertical slice implemented |
| Manual software maximum dimming | Basic setting |
| System tray controls | Implemented |
| Emergency hotkey | Implemented |
| Local deterministic luminance analysis | Implemented |
| Repetitive Flash Guard | Roadmap |
| Localized Flash Masking | Roadmap |
| StaticShade | Roadmap |
| Adaptive Content Dimming | Roadmap |
| Inactive Monitor Dimming | Roadmap |
| Focus Dimming | Roadmap |
| Hardware Brightness | Roadmap |
| Profiles, rules, schedules | Roadmap |

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
```

The CLI currently manages startup settings:

```powershell
dotnet run --project src/LumaBrake.Cli -- status
dotnet run --project src/LumaBrake.Cli -- disable
dotnet run --project src/LumaBrake.Cli -- safe-mode
dotnet run --project src/LumaBrake.Cli -- capture-test
```

## Safety

The overlay is click-through, non-activating, hidden from the taskbar, and asks Windows to exclude it from capture where supported. If capture or analysis fails, the controller clears active dimming before retrying.
