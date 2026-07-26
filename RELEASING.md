# Releasing

Release packaging currently produces a framework-dependent portable ZIP, SHA-256 checksum, and CycloneDX SBOM from CI.

Planned release flow:

1. Confirm `dotnet build` and `dotnet test`.
2. Download the `LumaBrake-win-x64-portable` workflow artifact.
3. Verify the portable ZIP checksum against `LumaBrake-win-x64-portable.zip.sha256`.
4. Review the generated `LumaBrake.cdx.json` SBOM.
5. Produce the installer artifact.
6. Attach release notes, portable ZIP, installer, checksums, and SBOM.
7. Tag the release.
