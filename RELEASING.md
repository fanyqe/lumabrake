# Releasing

Release packaging produces a self-contained `win-x64` portable ZIP, SHA-256 checksum, and CycloneDX SBOM from CI. Tags matching `v*` also create a GitHub Release and attach the ZIP, checksum, and SBOM. The portable package includes both the app and CLI; run `LumaBrake.Cli.exe startup enable` from that folder to register current-user login startup.

Planned release flow:

1. Confirm `dotnet build` and `dotnet test`.
2. Create and push an annotated `v*` tag.
3. Wait for the `release` workflow to build, test, publish, generate SBOM, and create the GitHub Release.
4. Download `LumaBrake-win-x64-portable.zip`, `LumaBrake-win-x64-portable.zip.sha256`, and `LumaBrake.cdx.json` from the release.
5. Verify the portable ZIP checksum against `LumaBrake-win-x64-portable.zip.sha256`.
6. Smoke-test `LumaBrake.Cli.exe startup status` from the extracted portable folder without installing the .NET runtime separately.
7. Produce the installer artifact when installer packaging is ready.
8. Attach installer artifacts and installer checksums to the same release.
