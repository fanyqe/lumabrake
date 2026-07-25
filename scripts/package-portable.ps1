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
$selfContainedValue = $SelfContained.ToString().ToLowerInvariant()
$artifactRootFullPath = [System.IO.Path]::GetFullPath($ArtifactRoot)
$portableRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRootFullPath "portable\LumaBrake-$Runtime"))
$pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$artifactRootWithSeparator = $artifactRootFullPath.TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar

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

if (!$portableRootFullPath.StartsWith($artifactRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Portable output must stay under the artifact root."
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
    if (Test-Path -LiteralPath $portableRootFullPath) {
        Remove-Item -LiteralPath $portableRootFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force $portableRootFullPath | Out-Null

    $dotnetPath = Resolve-DotNetCommand
    Invoke-NativeCommand -FilePath $dotnetPath -Arguments @(
        "publish",
        "src\LumaBrake.App\LumaBrake.App.csproj",
        "--configuration",
        $Configuration,
        "--runtime",
        $Runtime,
        "--self-contained",
        $selfContainedValue,
        "-p:Version=$normalizedVersion",
        "-p:AssemblyVersion=$normalizedVersion",
        "-p:FileVersion=$normalizedVersion",
        "-p:InformationalVersion=$normalizedVersion",
        "-p:IncludeSourceRevisionInInformationalVersion=false",
        "--output",
        $portableRootFullPath
    )

    Invoke-NativeCommand -FilePath $dotnetPath -Arguments @(
        "publish",
        "src\LumaBrake.Cli\LumaBrake.Cli.csproj",
        "--configuration",
        $Configuration,
        "--runtime",
        $Runtime,
        "--self-contained",
        $selfContainedValue,
        "-p:Version=$normalizedVersion",
        "-p:AssemblyVersion=$normalizedVersion",
        "-p:FileVersion=$normalizedVersion",
        "-p:InformationalVersion=$normalizedVersion",
        "-p:IncludeSourceRevisionInInformationalVersion=false",
        "--output",
        $portableRootFullPath
    )

    Copy-Item README.md, LICENSE, PRIVACY.md, SECURITY.md -Destination $portableRootFullPath
    if (Test-Path -LiteralPath (Join-Path $repoRoot "docs\assets")) {
        New-Item -ItemType Directory -Force (Join-Path $portableRootFullPath "docs") | Out-Null
        Copy-Item -LiteralPath (Join-Path $repoRoot "docs\assets") -Destination (Join-Path $portableRootFullPath "docs\assets") -Recurse -Force
    }

    & (Join-Path $PSScriptRoot "sign-windows-artifacts.ps1") `
        -Path $portableRootFullPath `
        -IncludeExtensions ".exe", ".dll" `
        -CertificatePath $SigningCertificatePath `
        -CertificatePassword $SigningCertificatePassword `
        -TimestampUrl $SigningTimestampUrl `
        -SignToolPath $SignToolPath `
        -SkipWhenCertificateMissing

    $zipPath = Join-Path $artifactRootFullPath "LumaBrake-$Runtime-portable.zip"
    Compress-Archive -Path (Join-Path $portableRootFullPath "*") -DestinationPath $zipPath -Force

    $hash = Get-FileHash $zipPath -Algorithm SHA256
    "$($hash.Hash)  LumaBrake-$Runtime-portable.zip" |
        Set-Content (Join-Path $artifactRootFullPath "LumaBrake-$Runtime-portable.zip.sha256") -Encoding ascii
}
finally {
    Pop-Location
}
