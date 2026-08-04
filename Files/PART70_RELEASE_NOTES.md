# Part 70 - Startup Experience and Reliability

## Startup splash
- Added a dedicated startup window with the application icon, animated loading ring, percentage, current stage, detail text, progress bar, and elapsed time.
- The splash remains visible until the local database, saved sites, settings, and offline screen snapshots are ready.

## Startup health checks
- Verifies and creates the application data, logs, and backup directories before opening the main window.
- Startup failures continue to use the global copyable error presenter.

## Startup history
- Writes a lightweight startup-history.log entry after a successful launch with timestamp, process ID, and initial working set.
- Startup telemetry never blocks application launch.

## Progress integration
- MainWindowViewModel now reports actual module-loading progress to the splash screen while loading offline SQLite data.
