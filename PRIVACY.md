# Privacy

LumaBrake processes screen samples locally.

- Screen content is not uploaded.
- Screen content is not stored permanently.
- No AI, OCR, content classification, or remote inference is used.
- The Phase 2 capture buffer is discarded after each luminance analysis pass.
- Local settings are stored under the current user's local application data folder.
- If Windows startup is enabled, the current user's Run registry entry stores only the local path to `LumaBrake.App.exe` plus the `--background` argument.

Future diagnostics export must redact paths and must not include raw screenshots by default.
