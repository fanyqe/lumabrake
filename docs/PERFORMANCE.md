# Performance Notes

Phase 4 analyzes a downscaled `160 x 90` BGRA sample per active monitor and derives a `16 x 9` tile luminance summary from the same sample. This keeps each detector buffer small and avoids full-resolution CPU analysis while the runtime moves toward a lower-latency capture backend.

Future baselines should track:

- capture interval;
- analysis duration;
- overlay update duration;
- allocation rate;
- dropped frames;
- CPU and GPU usage on one, two, and three monitors.
