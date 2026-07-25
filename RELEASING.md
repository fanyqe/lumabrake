# Releasing

Release packaging produces a self-contained `win-x64` portable ZIP, per-user MSI installer, SHA-256 checksums, and CycloneDX SBOM from CI. Existing annotated tags matching `v*` create a GitHub Release and attach the ZIP, MSI, checksums, and SBOM. Manual release workflow dispatch also requires an existing annotated `vX.Y.Z` tag and checks out that tag's commit before building. If `LUMABRAKE_SIGNING_CERTIFICATE_BASE64` and `LUMABRAKE_SIGNING_CERTIFICATE_PASSWORD` are configured, the release workflow signs the portable EXE/DLL payload before zipping, signs the MSI before checksum generation, and fails artifact verification if those signatures are missing or invalid. The portable package includes both the app and CLI; run `LumaBrake.Cli.exe startup enable` from that folder to register current-user login startup.

Planned release flow:

1. Confirm `dotnet format LumaBrake.sln --verify-no-changes`, `dotnet build`, and `dotnet test`.
2. Configure signing secrets for public releases: base64-encoded `.pfx` in `LUMABRAKE_SIGNING_CERTIFICATE_BASE64`, its password in `LUMABRAKE_SIGNING_CERTIFICATE_PASSWORD`, and optionally `LUMABRAKE_SIGNING_TIMESTAMP_URL`.
3. Create and push an annotated `v*` tag.
4. Wait for the `release` workflow to verify the tag, check out the tagged commit, build, test, publish, generate SBOM, and create the GitHub Release.
5. Download `LumaBrake-win-x64-portable.zip`, `LumaBrake-win-x64-portable.zip.sha256`, `LumaBrake-win-x64-setup.msi`, `LumaBrake-win-x64-setup.msi.sha256`, and `LumaBrake.cdx.json` from the release.
6. Verify the portable ZIP and MSI checksums.
7. Verify Authenticode signatures when signing secrets were configured.
8. Smoke-test the portable package without installing the .NET runtime separately with `scripts\test-portable-package.ps1 -PortableZipPath .\artifacts\LumaBrake-win-x64-portable.zip`.
9. Smoke-test MSI extraction with `scripts\test-installer-extract.ps1`.
10. Smoke-test MSI install and uninstall with `scripts\test-installer-install.ps1`.
11. Verify checksums, portable ZIP contents, app and CLI binary versions, SBOM JSON, and optionally signatures with `scripts\test-release-artifacts.ps1 -ArtifactRoot .\artifacts -ProductVersion <tag> [-RequireAuthenticodeSignature]`.
12. Run `scripts\test-field-smoke.ps1 -RequireMultipleMonitors` on real Windows hardware to verify packaged app startup, monitor enumeration, per-monitor capture sampling, DDC/CI read-only probing, diagnostics export, and the hardware-write confirmation gate.
13. Perform broader manual install and uninstall coverage across Windows editions, locked-down policy environments, SmartScreen behavior, HDR displays, protected content, exclusive full-screen apps, display reconnects, hotkeys, input pass-through, and long-session resource use before a stable public release.
