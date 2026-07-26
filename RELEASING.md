# Releasing

Release packaging currently produces a self-contained `win-x64` portable ZIP, SHA-256 checksum, and CycloneDX SBOM from CI. The portable package includes both the app and CLI; run `LumaBrake.Cli.exe startup enable` from that folder to register current-user login startup.

Planned release flow:

1. Confirm `dotnet build` and `dotnet test`.
2. Download the `LumaBrake-win-x64-portable` workflow artifact.
3. Verify the portable ZIP checksum against `LumaBrake-win-x64-portable.zip.sha256`.
4. Review the generated `LumaBrake.cdx.json` SBOM.
5. Smoke-test `LumaBrake.Cli.exe startup status` from the extracted portable folder without installing the .NET runtime separately.
6. Produce the installer artifact.
7. Attach release notes, portable ZIP, installer, checksums, and SBOM.
8. Tag the release.
