# Part 52.1 — Startup UI Freeze Fix

- Disabled full-window localization scanning during English startup.
- Runtime translation is now queued only when Arabic is active or the language changes.
- Pagination attaches lazily only to a visible DataGrid.
- Pager creation is deferred until WPF ApplicationIdle so the main shell paints first.
- Added re-parenting guards to prevent recursive Loaded/Unloaded attachment loops.
- Hidden screens no longer enumerate and filter their grids during startup.
