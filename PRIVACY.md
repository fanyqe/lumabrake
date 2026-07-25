# Privacy

LumaBrake processes screen samples locally.

- Screen content is not uploaded.
- Screen content is not stored permanently.
- No OCR, content classification, or screen recording is used.
- The Phase 2 capture buffer is discarded after each luminance analysis pass.
- Local settings are stored under the current user's local application data folder.
- If Windows startup is enabled, the current user's Run registry entry stores only the local path to `LumaBrake.App.exe` plus the `--background` argument.
- The MSI preserves local settings during uninstall by default.
- The MSI does not create a Windows startup entry. Startup remains a current-user dashboard or CLI setting.

Diagnostics export redacts filesystem paths and may include settings, monitor descriptors, runtime pipeline counters, active effect reasons, capture anomaly status, and HDR/advanced-color monitor state. It does not include raw screenshots, raw frames, recordings, OCR output, or screen content by default.
