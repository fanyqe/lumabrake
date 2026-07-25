param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,
    [string]$InstallRoot = (Join-Path $env:TEMP "lumabrake-msi-install"),
    [string]$LogRoot = (Join-Path $env:TEMP "lumabrake-msi-logs"),
    [string]$SettingsRoot = (Join-Path $env:TEMP "lumabrake-msi-settings")
)

$ErrorActionPreference = "Stop"
$installerFullPath = [System.IO.Path]::GetFullPath($InstallerPath)
$installRootFullPath = [System.IO.Path]::GetFullPath($InstallRoot)
$logRootFullPath = [System.IO.Path]::GetFullPath($LogRoot)
$settingsRootFullPath = [System.IO.Path]::GetFullPath($SettingsRoot)

if (!(Test-Path -LiteralPath $installerFullPath)) {
    throw "Installer MSI was not found."
}

if (Test-Path -LiteralPath $installRootFullPath) {
    throw "InstallRoot must not already exist."
}

if (Test-Path -LiteralPath $settingsRootFullPath) {
    throw "SettingsRoot must not already exist."
}

New-Item -ItemType Directory -Force $logRootFullPath | Out-Null
New-Item -ItemType Directory -Force $settingsRootFullPath | Out-Null

function Quote-Argument {
    param([string]$Value)

    '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-MsiExec {
    param(
        [string]$Arguments,
        [string]$Action
    )

    $process = Start-Process `
        -FilePath msiexec.exe `
        -ArgumentList $Arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden

    if ($process.ExitCode -ne 0) {
        throw "$Action failed with exit code $($process.ExitCode)."
    }
}

function Invoke-AppStartupSmoke {
    param([string]$AppPath)

    $previousSettingsRoot = $env:LUMABRAKE_SETTINGS_ROOT
    try {
        $env:LUMABRAKE_SETTINGS_ROOT = $settingsRootFullPath
        $process = Start-Process `
            -FilePath $AppPath `
            -ArgumentList @("--safe-mode", "--background", "--smoke-startup") `
            -PassThru `
            -WindowStyle Hidden

        if (!$process.WaitForExit(15000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Installed app smoke startup did not exit within 15 seconds."
        }

        if ($process.ExitCode -ne 0) {
            throw "Installed app smoke startup failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        if ($null -eq $previousSettingsRoot) {
            Remove-Item Env:\LUMABRAKE_SETTINGS_ROOT -ErrorAction SilentlyContinue
        } else {
            $env:LUMABRAKE_SETTINGS_ROOT = $previousSettingsRoot
        }
    }

    Write-Host "Installed app smoke startup passed."
}

$installLog = Join-Path $logRootFullPath "install.log"
$uninstallLog = Join-Path $logRootFullPath "uninstall.log"
$quotedInstaller = Quote-Argument $installerFullPath
$quotedInstallRoot = Quote-Argument $installRootFullPath
$quotedInstallLog = Quote-Argument $installLog
$quotedUninstallLog = Quote-Argument $uninstallLog
$installed = $false

try {
    Invoke-MsiExec `
        -Action "MSI install" `
        -Arguments "/i $quotedInstaller /qn INSTALLFOLDER=$quotedInstallRoot /L*v $quotedInstallLog"
    $installed = $true

    $appPath = Join-Path $installRootFullPath "LumaBrake.App.exe"
    $cliPath = Join-Path $installRootFullPath "LumaBrake.Cli.exe"
    $coreClrPath = Join-Path $installRootFullPath "coreclr.dll"

    if (!(Test-Path -LiteralPath $appPath) -or !(Test-Path -LiteralPath $cliPath) -or !(Test-Path -LiteralPath $coreClrPath)) {
        throw "Installed files are incomplete."
    }

    $startupValue = (Get-ItemProperty `
            -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
            -Name "LumaBrake" `
            -ErrorAction SilentlyContinue).LumaBrake
    if (![string]::IsNullOrWhiteSpace($startupValue) -and
        $startupValue.IndexOf($installRootFullPath, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "MSI must not create a per-user startup entry; use the dashboard or CLI startup command after install."
    }

    & $cliPath startup status | Out-Host
    Invoke-AppStartupSmoke -AppPath $appPath
}
finally {
    if ($installed) {
        Invoke-MsiExec `
            -Action "MSI uninstall" `
            -Arguments "/x $quotedInstaller /qn /L*v $quotedUninstallLog"
    }
}

if (Test-Path -LiteralPath (Join-Path $installRootFullPath "LumaBrake.App.exe")) {
    throw "MSI uninstall left the app executable behind."
}
