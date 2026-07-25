param(
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "tests\LumaBrake.Performance.Tests\LumaBrake.Performance.Tests.csproj"

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

Push-Location $repoRoot
try {
    $arguments = @(
        "test",
        $projectPath,
        "--configuration",
        $Configuration
    )

    if ($NoBuild) {
        $arguments += "--no-build"
    }

    $dotnetPath = Resolve-DotNetCommand
    & $dotnetPath @arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
