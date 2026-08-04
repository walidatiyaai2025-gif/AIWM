# Part 63 — Memory Cooling Mode

- Shows a global cooling badge whenever Windows physical-memory use reaches 80%.
- Uses 72% exit hysteresis so the warning does not flicker around the threshold.
- Pauses database-heavy live-dashboard refreshes while cooling is active.
- Releases hidden paged-grid caches and requests an optimized Gen-2 collection, throttled to once per 15 seconds.
- Keeps the UI responsive and leaves lightweight clock and memory metrics active.
- The cooling badge is non-blocking and disappears automatically after memory stabilizes.
