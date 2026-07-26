param(
    [string]$ArtifactRoot = (Join-Path $PWD "artifacts"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$selfContainedValue = $SelfContained.ToString().ToLowerInvariant()
$artifactRootFullPath = [System.IO.Path]::GetFullPath($ArtifactRoot)
$portableRootFullPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRootFullPath "portable\LumaBrake-$Runtime"))
$pathSeparators = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$artifactRootWithSeparator = $artifactRootFullPath.TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar

if (!$portableRootFullPath.StartsWith($artifactRootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Portable output must stay under the artifact root."
}

Push-Location $repoRoot
try {
    if (Test-Path -LiteralPath $portableRootFullPath) {
        Remove-Item -LiteralPath $portableRootFullPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force $portableRootFullPath | Out-Null

    dotnet publish src\LumaBrake.App\LumaBrake.App.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained $selfContainedValue `
        --output $portableRootFullPath

    dotnet publish src\LumaBrake.Cli\LumaBrake.Cli.csproj `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained $selfContainedValue `
        --output $portableRootFullPath

    Copy-Item README.md, LICENSE, PRIVACY.md, SECURITY.md -Destination $portableRootFullPath

    $zipPath = Join-Path $artifactRootFullPath "LumaBrake-$Runtime-portable.zip"
    Compress-Archive -Path (Join-Path $portableRootFullPath "*") -DestinationPath $zipPath -Force

    $hash = Get-FileHash $zipPath -Algorithm SHA256
    "$($hash.Hash)  LumaBrake-$Runtime-portable.zip" |
        Set-Content (Join-Path $artifactRootFullPath "LumaBrake-$Runtime-portable.zip.sha256") -Encoding ascii
}
finally {
    Pop-Location
}
