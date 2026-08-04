# Part 120 — Scheduler Compile Fix and Reliability Hardening

## Fixed
- Corrected the C# switch-expression member-access syntax in `SchedulerCenterViewModel.CalculateNextRunUtc` by wrapping the switch expression before calling `ToUniversalTime()`.
- Resolves `CS1002 ; expected` and `CS1513 } expected` reported at line 294.

## Added
- Duplicate-run prevention per schedule ID.
- Append-only scheduler execution history for both manual and scheduled runs.
- Outcome, start/end time, trigger source, site, task type, and failure details are recorded.
- Logging failures are isolated and never fail the scheduled task itself.

## Scheduler history path
`%LocalAppData%\AIWordPressManager\Scheduler\History\scheduler-history-<SiteId>.jsonl`

## Validation
Run from the solution root:

```powershell
dotnet clean
dotnet restore
dotnet build
```
