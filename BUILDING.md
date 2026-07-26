# Building

## Prerequisites

- Windows 10 or later.
- .NET 10 LTS SDK.
- PowerShell 5.1 or later.

## Commands

```powershell
dotnet restore
dotnet build LumaBrake.sln
dotnet test LumaBrake.sln --no-build
```

## Portable Package

The portable package is self-contained for `win-x64` and includes the .NET runtime needed to run the app.

```powershell
$artifactRoot = Join-Path $PWD "artifacts"
.\scripts\package-portable.ps1 -ArtifactRoot $artifactRoot -SelfContained $true
Get-FileHash (Join-Path $artifactRoot "LumaBrake-win-x64-portable.zip") -Algorithm SHA256
dotnet tool restore
dotnet dotnet-CycloneDX LumaBrake.sln --output $artifactRoot --filename LumaBrake.cdx.json --output-format Json --exclude-test-projects --set-name LumaBrake --set-version 0.0.0
```

## Local SDK

If the SDK is installed under the user profile, prepend it for the current shell:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```
