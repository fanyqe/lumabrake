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

```powershell
$artifactRoot = Join-Path $PWD "artifacts"
$portableRoot = Join-Path $artifactRoot "portable\LumaBrake-win-x64"
dotnet publish src\LumaBrake.App\LumaBrake.App.csproj --configuration Release --runtime win-x64 --self-contained false --output $portableRoot
dotnet publish src\LumaBrake.Cli\LumaBrake.Cli.csproj --configuration Release --runtime win-x64 --self-contained false --output $portableRoot
Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath (Join-Path $artifactRoot "LumaBrake-win-x64-portable.zip") -Force
Get-FileHash (Join-Path $artifactRoot "LumaBrake-win-x64-portable.zip") -Algorithm SHA256
dotnet tool restore
dotnet dotnet-CycloneDX LumaBrake.sln --output $artifactRoot --filename LumaBrake.cdx.json --output-format Json --exclude-test-projects --set-name LumaBrake --set-version 0.0.0
```

## Local SDK

If the SDK is installed under the user profile, prepend it for the current shell:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```
