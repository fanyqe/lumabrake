param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,
    [string]$ExtractRoot = (Join-Path $env:TEMP "lumabrake-msi-extract")
)

$ErrorActionPreference = "Stop"
$installerFullPath = [System.IO.Path]::GetFullPath($InstallerPath)
$extractRootFullPath = [System.IO.Path]::GetFullPath($ExtractRoot)

if (!(Test-Path -LiteralPath $installerFullPath)) {
    throw "Installer MSI was not found."
}

New-Item -ItemType Directory -Force $extractRootFullPath | Out-Null

function Quote-Argument {
    param([string]$Value)

    '"' + $Value.Replace('"', '\"') + '"'
}

$quotedInstaller = Quote-Argument $installerFullPath
$quotedExtractRoot = Quote-Argument $extractRootFullPath

$process = Start-Process `
    -FilePath msiexec.exe `
    -ArgumentList "/a $quotedInstaller /qn TARGETDIR=$quotedExtractRoot" `
    -Wait `
    -PassThru `
    -WindowStyle Hidden

if ($process.ExitCode -ne 0) {
    throw "MSI administrative extraction failed with exit code $($process.ExitCode)."
}

$app = Get-ChildItem -LiteralPath $extractRootFullPath -Recurse -Filter "LumaBrake.App.exe" |
    Select-Object -First 1
$cli = Get-ChildItem -LiteralPath $extractRootFullPath -Recurse -Filter "LumaBrake.Cli.exe" |
    Select-Object -First 1
$coreclr = Get-ChildItem -LiteralPath $extractRootFullPath -Recurse -Filter "coreclr.dll" |
    Select-Object -First 1

if ($null -eq $app -or $null -eq $cli -or $null -eq $coreclr) {
    throw "MSI extraction did not contain the app, CLI, and self-contained runtime."
}

& $cli.FullName startup status | Out-Host
