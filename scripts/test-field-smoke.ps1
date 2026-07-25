param(
    [string]$PackageRoot = (Join-Path $PWD "artifacts\portable\LumaBrake-win-x64"),
    [string]$SettingsRoot = "",
    [string]$DiagnosticsPath = "",
    [string]$EvidencePath = "",
    [switch]$RequireMultipleMonitors,
    [switch]$RequireHardwareBrightness,
    [switch]$RequireAuthenticodeSignature
)

$ErrorActionPreference = "Stop"

$packageRootFullPath = [System.IO.Path]::GetFullPath($PackageRoot)
if ([string]::IsNullOrWhiteSpace($SettingsRoot)) {
    $SettingsRoot = Join-Path $env:TEMP "lumabrake-field-smoke-settings-$([Guid]::NewGuid())"
}

if ([string]::IsNullOrWhiteSpace($DiagnosticsPath)) {
    $DiagnosticsPath = Join-Path $PWD "artifacts\diagnostics-field-smoke.zip"
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $PWD "artifacts\field-smoke-evidence.json"
}

$settingsRootFullPath = [System.IO.Path]::GetFullPath($SettingsRoot)
$diagnosticsFullPath = [System.IO.Path]::GetFullPath($DiagnosticsPath)
$evidenceFullPath = [System.IO.Path]::GetFullPath($EvidencePath)

if (!(Test-Path -LiteralPath $packageRootFullPath -PathType Container)) {
    throw "PackageRoot was not found."
}

if (Test-Path -LiteralPath $settingsRootFullPath) {
    throw "SettingsRoot must not already exist."
}

$appPath = Join-Path $packageRootFullPath "LumaBrake.App.exe"
$cliPath = Join-Path $packageRootFullPath "LumaBrake.Cli.exe"
$coreClrPath = Join-Path $packageRootFullPath "coreclr.dll"

if (!(Test-Path -LiteralPath $appPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $cliPath -PathType Leaf) -or
    !(Test-Path -LiteralPath $coreClrPath -PathType Leaf)) {
    throw "PackageRoot must contain LumaBrake.App.exe, LumaBrake.Cli.exe, and coreclr.dll."
}

New-Item -Path $settingsRootFullPath -ItemType Directory -Force | Out-Null
New-Item -Path ([System.IO.Path]::GetDirectoryName($diagnosticsFullPath)) -ItemType Directory -Force | Out-Null
New-Item -Path ([System.IO.Path]::GetDirectoryName($evidenceFullPath)) -ItemType Directory -Force | Out-Null

function Invoke-FieldCli {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [int[]]$AllowedExitCodes = @(0)
    )

    $previousSettingsRoot = $env:LUMABRAKE_SETTINGS_ROOT
    $previousLocation = (Get-Location).ProviderPath
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $env:LUMABRAKE_SETTINGS_ROOT = $settingsRootFullPath
        Set-Location -LiteralPath $packageRootFullPath
        $ErrorActionPreference = "Continue"
        $rawOutput = & $cliPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        $output = ($rawOutput | ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) {
                    $_.Exception.Message
                } else {
                    [string]$_
                }
            }) -join [Environment]::NewLine
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        Set-Location -LiteralPath $previousLocation
        if ($null -eq $previousSettingsRoot) {
            Remove-Item Env:\LUMABRAKE_SETTINGS_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:LUMABRAKE_SETTINGS_ROOT = $previousSettingsRoot
        }
    }

    if ($AllowedExitCodes -notcontains $exitCode) {
        throw "$Name failed with exit code $exitCode. output=$output"
    }

    Write-Host "--- $Name ---"
    Write-Host "exit=$exitCode"
    Write-Host $output

    [pscustomobject]@{
        name = $Name
        arguments = $Arguments
        exitCode = $exitCode
        output = $output.Trim()
    }
}

function Assert-AuthenticodeSignature {
    param(
        [string]$Path,
        [string]$DisplayName
    )

    $signature = Get-AuthenticodeSignature -FilePath $Path
    if ($signature.Status -ne "Valid") {
        throw "Authenticode signature is not valid for ${DisplayName}: $($signature.Status)."
    }

    $signature.Status.ToString()
}

function Get-DeviceNames {
    param([string]$Output)

    $matches = [regex]::Matches($Output, 'device="(?<device>(?:\\"|[^"])*)"')
    foreach ($match in $matches) {
        $match.Groups["device"].Value.Replace('\"', '"')
    }
}

function Get-MatchingLineCount {
    param(
        [string]$Output,
        [string]$Pattern
    )

    ([regex]::Matches($Output, $Pattern)).Count
}

function Assert-DiagnosticsPackage {
    param([string]$Path)

    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Diagnostics package was not created."
    }

    if ((Get-Item -LiteralPath $Path).Length -le 0) {
        throw "Diagnostics package is empty."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entryName in @("manifest.json", "settings.json", "monitors.json", "privacy.txt")) {
            if ($null -eq $archive.GetEntry($entryName)) {
                throw "Diagnostics package is missing $entryName."
            }
        }

        $manifestEntry = $archive.GetEntry("manifest.json")
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }

        if ($manifest.includesScreenContent -ne $false) {
            throw "Diagnostics manifest must declare includesScreenContent=false."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-AppSmoke {
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
            throw "App smoke startup did not exit within 15 seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "App smoke startup failed with exit code $($process.ExitCode)."
        }

        Write-Host "--- app smoke startup ---"
        Write-Host "exit=$($process.ExitCode)"
        return $process.ExitCode
    }
    finally {
        if ($null -eq $previousSettingsRoot) {
            Remove-Item Env:\LUMABRAKE_SETTINGS_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:LUMABRAKE_SETTINGS_ROOT = $previousSettingsRoot
        }
    }
}

$commands = New-Object System.Collections.Generic.List[object]
$commands.Add((Invoke-FieldCli -Name "cli status" -Arguments @("status")))
$monitorResult = Invoke-FieldCli -Name "monitor list" -Arguments @("monitor", "list")
$commands.Add($monitorResult)

$monitorDevices = @(Get-DeviceNames -Output $monitorResult.output)
if ($monitorDevices.Count -eq 0) {
    throw "No active Windows monitor was listed."
}

if ($RequireMultipleMonitors -and $monitorDevices.Count -lt 2) {
    throw "Multiple monitors were required, but only $($monitorDevices.Count) monitor(s) were listed."
}

$captureResult = Invoke-FieldCli -Name "capture-test all" -Arguments @("capture-test", "all")
$commands.Add($captureResult)
$availableCaptureCount = Get-MatchingLineCount -Output $captureResult.output -Pattern "Sampling quality: available"
if ($availableCaptureCount -lt $monitorDevices.Count) {
    throw "Capture sampling was not available for every listed monitor."
}

$hardwareListResult = Invoke-FieldCli -Name "hardware list" -Arguments @("hardware", "list")
$commands.Add($hardwareListResult)
$ddcCiCount = Get-MatchingLineCount -Output $hardwareListResult.output -Pattern 'ddcCi="yes"'

$brightnessResult = Invoke-FieldCli -Name "hardware brightness" -Arguments @("hardware", "brightness")
$commands.Add($brightnessResult)
$brightnessAvailableCount = Get-MatchingLineCount -Output $brightnessResult.output -Pattern 'available="yes"'
if ($RequireHardwareBrightness -and $brightnessAvailableCount -eq 0) {
    throw "Hardware brightness support was required, but no readable monitor was found."
}

$writesStatusResult = Invoke-FieldCli -Name "hardware writes status" -Arguments @("hardware", "writes", "status")
$commands.Add($writesStatusResult)
if ($writesStatusResult.output.IndexOf("Hardware brightness writes: disabled", [StringComparison]::Ordinal) -lt 0) {
    throw "Hardware brightness writes must be disabled by default."
}

$blockedWriteResult = Invoke-FieldCli `
    -Name "hardware brightness set without confirm" `
    -Arguments @("hardware", "brightness", "set", $monitorDevices[0], "0", "100") `
    -AllowedExitCodes @(2)
$commands.Add($blockedWriteResult)
if ($blockedWriteResult.output.IndexOf("--confirm-hardware-write", [StringComparison]::Ordinal) -lt 0) {
    throw "Hardware brightness set must require --confirm-hardware-write."
}

$commands.Add((Invoke-FieldCli -Name "hardware writes enable" -Arguments @("hardware", "writes", "enable")))
$plannedBrightnessCount = 0
foreach ($monitorDevice in $monitorDevices) {
    $planResult = Invoke-FieldCli `
        -Name "hardware brightness plan $monitorDevice" `
        -Arguments @("hardware", "brightness", "plan", $monitorDevice, "0", "80") `
        -AllowedExitCodes @(0, 1)
    $commands.Add($planResult)
    if ($planResult.exitCode -eq 0 -and $planResult.output.IndexOf('allowed="yes"', [StringComparison]::Ordinal) -ge 0) {
        $plannedBrightnessCount++
    }
}

$diagnosticsResult = Invoke-FieldCli -Name "diagnostics export" -Arguments @("diagnostics", "export", $diagnosticsFullPath)
$commands.Add($diagnosticsResult)
Assert-DiagnosticsPackage -Path $diagnosticsFullPath

$appExitCode = Invoke-AppSmoke

$signatures = [pscustomobject]@{
    required = [bool]$RequireAuthenticodeSignature
    app = if ($RequireAuthenticodeSignature) { Assert-AuthenticodeSignature -Path $appPath -DisplayName "LumaBrake.App.exe" } else { (Get-AuthenticodeSignature -FilePath $appPath).Status.ToString() }
    cli = if ($RequireAuthenticodeSignature) { Assert-AuthenticodeSignature -Path $cliPath -DisplayName "LumaBrake.Cli.exe" } else { (Get-AuthenticodeSignature -FilePath $cliPath).Status.ToString() }
}

$evidence = [pscustomobject]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    packageRoot = $packageRootFullPath
    settingsRoot = $settingsRootFullPath
    diagnosticsPath = $diagnosticsFullPath
    appFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appPath).FileVersion
    cliFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cliPath).FileVersion
    monitorCount = $monitorDevices.Count
    monitorDevices = $monitorDevices
    captureSamplingAvailableCount = $availableCaptureCount
    ddcCiCapabilityCount = $ddcCiCount
    hardwareBrightnessAvailableCount = $brightnessAvailableCount
    hardwareBrightnessPlanAllowedCount = $plannedBrightnessCount
    appSmokeExitCode = $appExitCode
    signatures = $signatures
    commands = $commands
}

$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidenceFullPath -Encoding UTF8
Write-Host "Field smoke evidence written: $evidenceFullPath"
