param(
    [Parameter(Mandatory = $true)]
    [string]$PortableZipPath,
    [string]$ExtractRoot = (Join-Path $env:TEMP "lumabrake-portable-smoke"),
    [string]$SettingsRoot = (Join-Path $env:TEMP "lumabrake-portable-smoke-settings")
)

$ErrorActionPreference = "Stop"

$portableZipFullPath = [System.IO.Path]::GetFullPath($PortableZipPath)
$extractRootFullPath = [System.IO.Path]::GetFullPath($ExtractRoot)
$settingsRootFullPath = [System.IO.Path]::GetFullPath($SettingsRoot)

if (!(Test-Path -LiteralPath $portableZipFullPath -PathType Leaf)) {
    throw "Portable ZIP was not found."
}

if (Test-Path -LiteralPath $extractRootFullPath) {
    throw "ExtractRoot must not already exist."
}

if (Test-Path -LiteralPath $settingsRootFullPath) {
    throw "SettingsRoot must not already exist."
}

if ($extractRootFullPath.Equals($settingsRootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "ExtractRoot and SettingsRoot must be different directories."
}

New-Item -Path $extractRootFullPath -ItemType Directory -Force | Out-Null
New-Item -Path $settingsRootFullPath -ItemType Directory -Force | Out-Null
Expand-Archive -LiteralPath $portableZipFullPath -DestinationPath $extractRootFullPath -Force

$appPath = Join-Path $extractRootFullPath "LumaBrake.App.exe"
$cliPath = Join-Path $extractRootFullPath "LumaBrake.Cli.exe"
$coreClrPath = Join-Path $extractRootFullPath "coreclr.dll"

if (!(Test-Path -LiteralPath $appPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $cliPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $coreClrPath -PathType Leaf)) {
    throw "Portable package did not contain the app, CLI, and self-contained runtime."
}

function Invoke-PortableCli {
    param(
        [string[]]$Arguments,
        [string]$ExpectedOutput
    )

    $previousSettingsRoot = $env:LUMABRAKE_SETTINGS_ROOT
    $previousLocation = (Get-Location).ProviderPath
    try {
        $env:LUMABRAKE_SETTINGS_ROOT = $settingsRootFullPath
        Set-Location -LiteralPath $extractRootFullPath
        $output = & $cliPath @Arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        Set-Location -LiteralPath $previousLocation
        if ($null -eq $previousSettingsRoot) {
            Remove-Item Env:\LUMABRAKE_SETTINGS_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:LUMABRAKE_SETTINGS_ROOT = $previousSettingsRoot
        }
    }

    if ($exitCode -ne 0) {
        throw "Portable CLI failed with exit code $exitCode. output=$output"
    }

    if (![string]::IsNullOrWhiteSpace($ExpectedOutput) -and
        $output.IndexOf($ExpectedOutput, [StringComparison]::Ordinal) -lt 0) {
        throw "Portable CLI output did not contain '$ExpectedOutput'. output=$output"
    }

    Write-Host $output
}

function Invoke-PortableAppSmoke {
    $previousSettingsRoot = $env:LUMABRAKE_SETTINGS_ROOT
    try {
        $env:LUMABRAKE_SETTINGS_ROOT = $settingsRootFullPath
        $process = Start-Process `
            -FilePath $appPath `
            -ArgumentList @("--safe-mode", "--background", "--smoke-startup") `
            -PassThru `
            -WindowStyle Hidden

        if (!$process.WaitForExit(15000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Portable app smoke startup did not exit within 15 seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "Portable app smoke startup failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        if ($null -eq $previousSettingsRoot) {
            Remove-Item Env:\LUMABRAKE_SETTINGS_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:LUMABRAKE_SETTINGS_ROOT = $previousSettingsRoot
        }
    }

    Write-Host "Portable app smoke startup passed."
}

Invoke-PortableCli -Arguments @("status") -ExpectedOutput "LumaBrake settings: enabled"
Invoke-PortableCli -Arguments @("night-color", "manual", "3400", "80") -ExpectedOutput "Night color manual temperature: 3400K"
Invoke-PortableCli -Arguments @("night-color", "status") -ExpectedOutput "Night color: enabled"
Invoke-PortableCli -Arguments @("startup", "status") -ExpectedOutput "Windows startup:"
Invoke-PortableAppSmoke
Write-Host "Portable package smoke test passed under $extractRootFullPath"
