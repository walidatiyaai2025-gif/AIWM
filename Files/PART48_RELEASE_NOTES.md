# Part 48 — Execution Planner + Execution Center Reliability

## Execution Center reliability
- Action buttons remain clickable while the center is idle and now explain why an operation cannot run instead of appearing broken.
- Multi-selection behavior synchronizes both directions between the DataGrid and ViewModel.
- Programmatic selection is reflected visually in the grid.
- Pending proposals can be approved even when they require a future adapter or staging; unsafe items remain blocked from direct execution.
- Added clear no-op messages for execute, retry, rollback, and select-ready actions.

## Phase 48 — Execution Planner
- Added deterministic planning directly to the existing Execution Center instead of creating another unfinished screen.
- Classifies queue rows into Safe, Review, and Manual/Staging groups.
- Added Build execution plan.
- Added Approve all low risk.
- Added Run safe plan, which approves eligible low-risk rows, reloads the queue, then executes only supported direct changes.
- Existing backup, WordPress update, read-back verification, Jobs history, cancellation, retry, and rollback pipelines are preserved.

## UI
- Stronger button text contrast.
- Disabled buttons remain readable.
- Planner summary and live counts appear inside Execution Center.
