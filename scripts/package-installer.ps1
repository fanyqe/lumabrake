param(
    [string]$ArtifactRoot = (Join-Path $PWD "artifacts"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$ProductVersion = "0.1.0",
    [bool]$SelfContained = $true,
    [string]$SigningCertificatePath = "",
    [string]$SigningCertificatePassword = "",
    [string]$SigningTimestampUrl = "http://timestamp.digicert.com",
    [string]$SignToolPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRootFullPath = [System.IO.Path]::GetFullPath($ArtifactRoot)
$payloadFullPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRootFullPath "portable\LumaBrake-$Runtime"))
$installerOutputFullPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRootFullPath "installer"))
$installerProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "installer\LumaBrake.Installer"))
$pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$artifactRootWithSeparator = $artifactRootFullPath.TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar
$installerProjectRootWithSeparator = $installerProjectRoot.TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar

function Resolve-DotNetCommand {
    $userDotNetPath = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $userDotNetPath -PathType Leaf) {
        return $userDotNetPath
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand -and ![string]::IsNullOrWhiteSpace($dotnetCommand.Source)) {
        return $dotnetCommand.Source
    }

    throw "dotnet SDK command was not found."
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Assert-ChildPath {
    param(
        [string]$Path,
        [string]$ParentWithSeparator
    )

    if (!$Path.StartsWith($ParentWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer outputs must stay under the artifact root."
    }
}

Assert-ChildPath -Path $payloadFullPath -ParentWithSeparator $artifactRootWithSeparator
Assert-ChildPath -Path $installerOutputFullPath -ParentWithSeparator $artifactRootWithSeparator

function Clear-InstallerBuildState {
    foreach ($relativePath in @("bin", "obj")) {
        $statePath = [System.IO.Path]::GetFullPath((Join-Path $installerProjectRoot $relativePath))
        if (!$statePath.StartsWith($installerProjectRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer build-state cleanup path escaped the installer project root."
        }

        if (Test-Path -LiteralPath $statePath) {
            Remove-Item -LiteralPath $statePath -Recurse -Force
        }
    }
}

$normalizedVersion = $ProductVersion.Trim()
if ($normalizedVersion.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}

if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "ProductVersion must be an MSI-compatible version like 0.1.0."
}

Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot "package-portable.ps1") `
        -ArtifactRoot $artifactRootFullPath `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -ProductVersion $normalizedVersion `
        -SelfContained $SelfContained `
        -SigningCertificatePath $SigningCertificatePath `
        -SigningCertificatePassword $SigningCertificatePassword `
        -SigningTimestampUrl $SigningTimestampUrl `
        -SignToolPath $SignToolPath

    $appPath = Join-Path $payloadFullPath "LumaBrake.App.exe"
    $cliPath = Join-Path $payloadFullPath "LumaBrake.Cli.exe"
    if (!(Test-Path -LiteralPath $appPath) -or !(Test-Path -LiteralPath $cliPath)) {
        throw "Portable payload is incomplete; app and CLI executables are required."
    }

    if (Test-Path -LiteralPath $installerOutputFullPath) {
        Remove-Item -LiteralPath $installerOutputFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force $installerOutputFullPath | Out-Null
    Clear-InstallerBuildState

    $dotnetPath = Resolve-DotNetCommand
    $wixProject = Join-Path $repoRoot "installer\LumaBrake.Installer\LumaBrake.Installer.wixproj"
    Invoke-NativeCommand -FilePath $dotnetPath -Arguments @(
        "build",
        $wixProject,
        "--configuration",
        $Configuration,
        "--no-incremental",
        "-p:PayloadDir=$payloadFullPath",
        "-p:ProductVersion=$normalizedVersion",
        "-p:RuntimeIdentifier=$Runtime",
        "-p:Platform=x64",
        "-p:InstallerPlatform=x64",
        "-p:OutputPath=$installerOutputFullPath\"
    )

    $builtMsi = Get-ChildItem -LiteralPath $installerOutputFullPath -Recurse -Filter "*.msi" |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $builtMsi) {
        throw "Installer build did not produce an MSI."
    }

    $installerPath = Join-Path $artifactRootFullPath "LumaBrake-$Runtime-setup.msi"
    Copy-Item -LiteralPath $builtMsi.FullName -Destination $installerPath -Force

    & (Join-Path $PSScriptRoot "sign-windows-artifacts.ps1") `
        -Path $installerPath `
        -IncludeExtensions ".msi" `
        -CertificatePath $SigningCertificatePath `
        -CertificatePassword $SigningCertificatePassword `
        -TimestampUrl $SigningTimestampUrl `
        -SignToolPath $SignToolPath `
        -SkipWhenCertificateMissing

    $hash = Get-FileHash $installerPath -Algorithm SHA256
    "$($hash.Hash)  LumaBrake-$Runtime-setup.msi" |
        Set-Content (Join-Path $artifactRootFullPath "LumaBrake-$Runtime-setup.msi.sha256") -Encoding ascii

    Invoke-NativeCommand -FilePath $dotnetPath -Arguments @("tool", "restore")
    Invoke-NativeCommand -FilePath $dotnetPath -Arguments @(
        "dotnet-CycloneDX",
        "LumaBrake.sln",
        "--output",
        $artifactRootFullPath,
        "--filename",
        "LumaBrake.cdx.json",
        "--output-format",
        "Json",
        "--exclude-test-projects",
        "--set-name",
        "LumaBrake",
        "--set-version",
        $normalizedVersion
    )
}
finally {
    Pop-Location
}
