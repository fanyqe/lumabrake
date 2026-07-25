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

## Release Packages

The portable package and per-user MSI installer are self-contained for `win-x64` and include the .NET runtime needed to run the app.

```powershell
$artifactRoot = Join-Path $PWD "artifacts"
.\scripts\package-installer.ps1 -ArtifactRoot $artifactRoot -ProductVersion 0.1.0 -SelfContained $true
.\scripts\test-installer-extract.ps1 -InstallerPath (Join-Path $artifactRoot "LumaBrake-win-x64-setup.msi") -ExtractRoot (Join-Path $artifactRoot "msi-extract")
.\scripts\test-installer-install.ps1 -InstallerPath (Join-Path $artifactRoot "LumaBrake-win-x64-setup.msi") -InstallRoot (Join-Path $env:TEMP "lumabrake-msi-install") -LogRoot (Join-Path $artifactRoot "msi-logs")
Get-FileHash (Join-Path $artifactRoot "LumaBrake-win-x64-portable.zip") -Algorithm SHA256
Get-FileHash (Join-Path $artifactRoot "LumaBrake-win-x64-setup.msi") -Algorithm SHA256
dotnet tool restore
dotnet dotnet-CycloneDX LumaBrake.sln --output $artifactRoot --filename LumaBrake.cdx.json --output-format Json --exclude-test-projects --set-name LumaBrake --set-version 0.1.0
.\scripts\test-release-artifacts.ps1 -ArtifactRoot $artifactRoot -ProductVersion 0.1.0
```

Release packaging can Authenticode-sign the portable payload and MSI when a trusted `.pfx` certificate is provided. The signing step is skipped when `-SigningCertificatePath` is empty.

```powershell
.\scripts\package-installer.ps1 `
  -ArtifactRoot $artifactRoot `
  -ProductVersion 0.1.0 `
  -SelfContained $true `
  -SigningCertificatePath "C:\secure\lumabrake-signing.pfx" `
  -SigningCertificatePassword $env:LUMABRAKE_SIGNING_CERTIFICATE_PASSWORD
```

## Local SDK

If the SDK is installed under the user profile, prepend it for the current shell:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```
