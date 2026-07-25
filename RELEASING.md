# Releasing

Release packaging currently produces a framework-dependent portable ZIP from CI.

Planned release flow:

1. Confirm `dotnet build` and `dotnet test`.
2. Download the `LumaBrake-win-x64-portable` workflow artifact.
3. Verify the portable ZIP checksum against `LumaBrake-win-x64-portable.zip.sha256`.
4. Produce installer and SBOM artifacts.
5. Attach release notes, portable ZIP, installer, checksums, and SBOM.
6. Tag the release.
