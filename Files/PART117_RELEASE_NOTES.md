# Part 117 — WordPress Transaction Center and Crash Recovery

- Added an append-only transaction journal viewer.
- Groups Started, Committed, Failed, and RecoveryReview events by Transaction ID.
- Detects interrupted transactions after ten minutes without a terminal event.
- Adds safe reconciliation that never repeats a WordPress write automatically.
- Adds search, state filters, CSV export, raw JSON copy, detailed event timeline, and direct navigation to Execution Center, Evidence, and API Logs.
- Filters transactions to the selected site when a site is active.
- Added Ribbon navigation and Arabic/English page metadata.
