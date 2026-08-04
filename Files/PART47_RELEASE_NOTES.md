# Part 47 — Live Dashboard and Working Execution Center

## Execution Center
- Added a real persisted ExecutionJob record for every execute, retry, and rollback batch.
- Execution jobs now report progress and current step into SQLite.
- Current jobs register with the cancellation registry and can be cancelled from Execution Center or Jobs.
- Added Execute all ready, Retry failed, Execute selected, Rollback selected, and Cancel current.
- Added queue state, failed count, last execution time, and five operational summary cards.
- The pipeline remains Validate/Backup/Execute/Read-back/Verify through the existing approved-change service.

## Live dashboard
- Added a one-second live clock and heartbeat.
- Dashboard polls job state every three seconds.
- Shows running and failed jobs, live execution step, overall execution progress, and last refresh time.
- The live strip updates from SQLite and the active Execution Center operation rather than demo numbers.

## Safety
- Only approved supported changes execute.
- High-risk and staging-required changes remain blocked.
- SQLite backup and WordPress read-back verification remain mandatory.
