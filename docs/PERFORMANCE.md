# Performance Notes

Phase 2 analyzes a downscaled `160 x 90` BGRA sample from the primary monitor. This keeps the detector buffer small and avoids full-resolution CPU analysis.

Future baselines should track:

- capture interval;
- analysis duration;
- overlay update duration;
- allocation rate;
- dropped frames;
- CPU and GPU usage on one, two, and three monitors.
