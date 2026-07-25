param(
    [string]$ArtifactRoot = (Join-Path $PWD "artifacts"),
    [string]$Runtime = "win-x64",
    [string]$ProductVersion = "",
    [switch]$RequireAuthenticodeSignature
)

$ErrorActionPreference = "Stop"
$artifactRootFullPath = [System.IO.Path]::GetFullPath($ArtifactRoot)

function Resolve-Artifact {
    param([string]$Name)

    $path = Join-Path $artifactRootFullPath $Name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Release artifact is missing: $Name."
    }

    return $path
}

function Assert-Checksum {
    param(
        [string]$ArtifactName,
        [string]$ChecksumName
    )

    $artifactPath = Resolve-Artifact $ArtifactName
    $checksumPath = Resolve-Artifact $ChecksumName
    $line = (Get-Content -LiteralPath $checksumPath -Raw).Trim()

    if ($line -notmatch '^(?<hash>[A-Fa-f0-9]{64})\s+\S+$') {
        throw "Checksum file has an invalid format: $ChecksumName."
    }

    $expected = $Matches["hash"].ToUpperInvariant()
    $actual = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($expected -ne $actual) {
        throw "Checksum mismatch for $ArtifactName."
    }

    Write-Host "Checksum OK: $ArtifactName"
}

function Assert-ZipEntry {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$EntryName
    )

    if ($null -eq $Archive.GetEntry($EntryName)) {
        throw "Portable ZIP is missing required entry: $EntryName."
    }
}

function Normalize-ProductVersion {
    param([string]$Value)

    $normalized = $Value.Trim()
    if ($normalized.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    if ($normalized -notmatch '^\d+\.\d+\.\d+$') {
        throw "ProductVersion must be an MSI-compatible version like 0.1.0."
    }

    return $normalized
}

function Assert-ZipExecutableVersion {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$EntryName,
        [string]$ExpectedVersion,
        [string]$TempRoot
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Portable ZIP is missing required entry: $EntryName."
    }

    $targetPath = Join-Path $TempRoot $EntryName
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetPath, $true)
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($targetPath)
    if ($versionInfo.FileVersion -ne $ExpectedVersion) {
        throw "$EntryName FileVersion '$($versionInfo.FileVersion)' did not match expected version '$ExpectedVersion'."
    }

    if ($versionInfo.ProductVersion -ne $ExpectedVersion) {
        throw "$EntryName ProductVersion '$($versionInfo.ProductVersion)' did not match expected version '$ExpectedVersion'."
    }
}

function Assert-AuthenticodeVerifierAvailable {
    if ($null -eq (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
        throw "Get-AuthenticodeSignature is not available on this host."
    }
}

function Assert-AuthenticodeSignature {
    param(
        [string]$Path,
        [string]$DisplayName
    )

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne "Valid") {
        $message = $signature.StatusMessage
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = "no status message"
        }

        throw "Authenticode signature is not valid for ${DisplayName}: $($signature.Status) ($message)."
    }

    Write-Host "Authenticode signature OK: $DisplayName"
}

function Assert-ZipAuthenticodeSignatures {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [string]$TempRoot
    )

    $pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $tempRootWithSeparator = [System.IO.Path]::GetFullPath($TempRoot).TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar
    $checkedCount = 0

    foreach ($entry in $Archive.Entries) {
        if ([string]::IsNullOrWhiteSpace($entry.Name)) {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
        if (@(".exe", ".dll") -notcontains $extension) {
            continue
        }

        $targetPath = Join-Path $TempRoot $entry.FullName
        $targetFullPath = [System.IO.Path]::GetFullPath($targetPath)
        if (!$targetFullPath.StartsWith($tempRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Portable ZIP entry has an unsafe path: $($entry.FullName)."
        }

        $targetDirectory = [System.IO.Path]::GetDirectoryName($targetFullPath)
        New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetFullPath, $true)
        Assert-AuthenticodeSignature -Path $targetFullPath -DisplayName $entry.FullName
        $checkedCount++
    }

    if ($checkedCount -eq 0) {
        throw "Portable ZIP did not contain EXE or DLL entries for Authenticode verification."
    }
}

$portableName = "LumaBrake-$Runtime-portable.zip"
$portableChecksumName = "$portableName.sha256"
$installerName = "LumaBrake-$Runtime-setup.msi"
$installerChecksumName = "$installerName.sha256"
$expectedProductVersion = $null
if (![string]::IsNullOrWhiteSpace($ProductVersion)) {
    $expectedProductVersion = Normalize-ProductVersion $ProductVersion
}

Assert-Checksum -ArtifactName $portableName -ChecksumName $portableChecksumName
Assert-Checksum -ArtifactName $installerName -ChecksumName $installerChecksumName

if ($RequireAuthenticodeSignature) {
    Assert-AuthenticodeVerifierAvailable
    Assert-AuthenticodeSignature -Path (Resolve-Artifact $installerName) -DisplayName $installerName
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$portablePath = Resolve-Artifact $portableName
$archive = [System.IO.Compression.ZipFile]::OpenRead($portablePath)
$verificationTempRoot = $null
try {
    Assert-ZipEntry -Archive $archive -EntryName "LumaBrake.App.exe"
    Assert-ZipEntry -Archive $archive -EntryName "LumaBrake.Cli.exe"
    Assert-ZipEntry -Archive $archive -EntryName "coreclr.dll"
    Assert-ZipEntry -Archive $archive -EntryName "README.md"
    Assert-ZipEntry -Archive $archive -EntryName "LICENSE"
    Assert-ZipEntry -Archive $archive -EntryName "PRIVACY.md"
    Assert-ZipEntry -Archive $archive -EntryName "SECURITY.md"

    if ($null -ne $expectedProductVersion -or $RequireAuthenticodeSignature) {
        $verificationTempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "lumabrake-artifact-check-$([Guid]::NewGuid())"
        New-Item -ItemType Directory -Force $verificationTempRoot | Out-Null
    }

    if ($null -ne $expectedProductVersion) {
        Assert-ZipExecutableVersion -Archive $archive -EntryName "LumaBrake.App.exe" -ExpectedVersion $expectedProductVersion -TempRoot $verificationTempRoot
        Assert-ZipExecutableVersion -Archive $archive -EntryName "LumaBrake.Cli.exe" -ExpectedVersion $expectedProductVersion -TempRoot $verificationTempRoot
    }

    if ($RequireAuthenticodeSignature) {
        Assert-ZipAuthenticodeSignatures -Archive $archive -TempRoot $verificationTempRoot
    }
}
finally {
    $archive.Dispose()
    if ($null -ne $verificationTempRoot -and (Test-Path -LiteralPath $verificationTempRoot)) {
        Remove-Item -LiteralPath $verificationTempRoot -Recurse -Force
    }
}

$sbomPath = Resolve-Artifact "LumaBrake.cdx.json"
$sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
if ($null -eq $sbom -or $sbom.bomFormat -ne "CycloneDX") {
    throw "SBOM JSON does not look like a CycloneDX document."
}

if ($null -ne $expectedProductVersion -and $sbom.metadata.component.version -ne $expectedProductVersion) {
    throw "SBOM version '$($sbom.metadata.component.version)' did not match expected version '$expectedProductVersion'."
}

Write-Host "Release artifacts verified under $artifactRootFullPath"
