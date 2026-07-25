param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [string[]]$IncludeExtensions = @(".exe", ".dll", ".msi"),
    [string]$CertificatePath = "",
    [string]$CertificatePassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$SignToolPath = "",
    [switch]$SkipWhenCertificateMissing
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    if ($SkipWhenCertificateMissing) {
        Write-Host "Code signing skipped; signing certificate path was not provided."
        return
    }

    throw "Signing certificate path is required."
}

function Resolve-SignTool {
    param([string]$RequestedPath)

    if (![string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequestedPath = [System.IO.Path]::GetFullPath($RequestedPath)
        if (!(Test-Path -LiteralPath $resolvedRequestedPath -PathType Leaf)) {
            throw "signtool.exe was not found at $resolvedRequestedPath."
        }

        return $resolvedRequestedPath
    }

    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command -and !( [string]::IsNullOrWhiteSpace($command.Source))) {
        return $command.Source
    }

    $windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $windowsKitsRoot -PathType Container) {
        $sdkSignTool = Get-ChildItem -Path (Join-Path $windowsKitsRoot "*\x64\signtool.exe") -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -ne $sdkSignTool) {
            return $sdkSignTool.FullName
        }
    }

    throw "signtool.exe was not found. Install the Windows SDK or pass -SignToolPath."
}

function Resolve-SignableFiles {
    param(
        [string[]]$InputPath,
        [string[]]$Extensions
    )

    $extensionLookup = @{}
    foreach ($extension in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($extension)) {
            continue
        }

        $normalizedExtension = $extension.Trim().ToLowerInvariant()
        if (!$normalizedExtension.StartsWith(".")) {
            $normalizedExtension = ".$normalizedExtension"
        }

        $extensionLookup[$normalizedExtension] = $true
    }

    $files = @{}
    foreach ($item in $InputPath) {
        $resolvedPath = [System.IO.Path]::GetFullPath($item)
        if (Test-Path -LiteralPath $resolvedPath -PathType Container) {
            Get-ChildItem -LiteralPath $resolvedPath -Recurse -File |
                Where-Object { $extensionLookup.ContainsKey($_.Extension.ToLowerInvariant()) } |
                ForEach-Object { $files[$_.FullName] = $_ }
            continue
        }

        if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
            $file = Get-Item -LiteralPath $resolvedPath
            if ($extensionLookup.ContainsKey($file.Extension.ToLowerInvariant())) {
                $files[$file.FullName] = $file
            }
            continue
        }

        throw "Signable path does not exist: $resolvedPath."
    }

    return $files.Values | Sort-Object FullName
}

$certificateFullPath = [System.IO.Path]::GetFullPath($CertificatePath)
if (!(Test-Path -LiteralPath $certificateFullPath -PathType Leaf)) {
    throw "Signing certificate was not found at $certificateFullPath."
}

$resolvedSignToolPath = Resolve-SignTool -RequestedPath $SignToolPath
$signableFiles = Resolve-SignableFiles -InputPath $Path -Extensions $IncludeExtensions
if ($signableFiles.Count -eq 0) {
    throw "No signable artifacts were found."
}

if ([string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $TimestampUrl = "http://timestamp.digicert.com"
}

foreach ($file in $signableFiles) {
    $arguments = @(
        "sign",
        "/f",
        $certificateFullPath,
        "/fd",
        "SHA256",
        "/tr",
        $TimestampUrl,
        "/td",
        "SHA256"
    )

    if (![string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $arguments += @("/p", $CertificatePassword)
    }

    $arguments += $file.FullName
    & $resolvedSignToolPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed for $($file.FullName)."
    }

    Write-Host "Signed: $($file.Name)"
}
