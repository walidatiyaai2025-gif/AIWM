# Part 62 — Pagination StackOverflow Fix + Universal Grid Phase 1

## Fixed
- Prevented re-entrant CollectionView refresh loops in `PagedDataGridBehavior`.
- Added `_isRefreshing` and queued refresh guards.
- Collection change notifications are now deferred to the dispatcher.
- Removed the full filtered-list copy; pagination now uses two low-memory passes and retains only the visible page.
- Hidden-grid cache release no longer forces an extra refresh.

## Universal Grid Phase 1
Every paged DataGrid now has a context menu with:
- Copy current cell.
- Copy selected rows with headers.
- Export the currently visible page to UTF-8 CSV.
- Auto-size columns.

## Safety
- No WordPress data is modified by these grid operations.
- Export and copy operations work only on data already loaded into the application.
