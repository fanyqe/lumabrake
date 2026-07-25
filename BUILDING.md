# Building

## Prerequisites

- Windows 10 or later.
- .NET 10 LTS SDK.
- PowerShell 5.1 or later.

## Commands

```powershell
dotnet restore
dotnet build LumaBrake.sln
dotnet test LumaBrake.sln --no-build
```

## Local SDK

If the SDK is installed under the user profile, prepend it for the current shell:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
```
