# Part 96 — Executable Navigation Cards and Universal Grid Actions

## Implemented
- Dashboard metric cards now navigate to the related operational screen.
- API log summary cards refresh/open the related data.
- Added a direct navigation button from API Logs to the site connection test.
- Right-clicking a row selects it before opening the menu.
- Every paged DataGrid now receives universal actions: copy rows/cell/JSON, export page/selection, auto-size/reset columns.
- The context menu also discovers commands exposed by the owning screen ViewModel and shows only commands currently executable for the selected row.
- Existing screen-specific context menus are preserved.

## WordPress execution truth
Execution Center sends writes only for approved, supported content actions: SetTitle, SetSlug, SetExcerpt, SetStatus, and SetContent. It creates a SQLite backup, reads the live WordPress object, sends the REST update, reads it again, and verifies the saved value. Unsupported, staging-required, high-risk, and unapproved actions remain blocked.
