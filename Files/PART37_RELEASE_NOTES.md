# Part 37 — Live Background Jobs and Notifications

## Implemented

- Replaced the Jobs placeholder with a complete operational screen.
- Loads durable job history from SQLite for the selected site at application startup.
- Polls SQLite every three seconds to refresh running progress and final state.
- Added status cards for Running, Completed, Failed, and Cancelled jobs.
- Added status filtering and full-text search across site, job type, step, and saved error details.
- Added selected-job diagnostics with saved error details and duration.
- Added real cancellation registration for active WordPress synchronization jobs.
- Added retry support for failed or cancelled WordPress synchronization jobs.
- Added a notification badge and session notification summary to the top bar.
- Extended IExecutionJobStore with read-only recent-history queries.
- Preserved all existing execution history; no database schema migration was required.

## Safety behavior

- Cancel is enabled only while a running job is registered in the current application process.
- Retry creates a new synchronization job and preserves the previous failed/cancelled record.
- Completed work and audit history remain stored in SQLite after cancellation.
- Unsupported job types are not falsely retried from the Jobs screen.

## Validation performed in the generation environment

- MainWindow.xaml parsed successfully as XML.
- No absolute Part1/Downloads project paths were introduced.
- .NET SDK was not available in the generation environment, so build and tests must be run on the target Windows development machine.
