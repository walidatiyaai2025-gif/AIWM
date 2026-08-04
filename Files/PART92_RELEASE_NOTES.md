# Part 92 — Startup Splash Deadlock Prevention

- Startup now loads only six dashboard-critical modules before opening the main window.
- Every startup module has a four-second timeout so one screen cannot hold the splash forever.
- Content Planner and the other non-essential screens now hydrate after the main window is visible.
- Deferred screen loads have independent eight-second limits and remain manually refreshable.
- The splash still respects the configured minimum duration of at least three seconds.
- Timeout failures are reported in the application status rather than blocking startup.
