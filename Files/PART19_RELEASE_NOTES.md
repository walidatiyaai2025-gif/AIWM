# Phase 1 Part 19 — Gold Offline Bulk

## Global black/gold design
- Replaced the former white/purple visual system with black surfaces and gold primary/secondary text.
- Added gold-aware DataGrid, Tab, ComboBox, input, selection, focus, hover, and button states.
- Added consistent selected-row styling and gold status accents.

## Offline-first behavior
- WordPress Explorer, Deletion Center, Post SEO Editor, Category Planner, Internal Links, Suggested Changes, Sites, and Settings continue to load SQLite data first.
- UI explicitly shows that SQLite is the active source while synchronization refreshes the cache in the background.
- Failed synchronization does not remove the last valid local snapshot.

## Multi-select and bulk operations
- Added reusable DataGrid selected-items behavior.
- Post SEO Editor supports Ctrl/Shift multi-select and bulk changes for status, categories, tags, comments, pings, and sticky state.
- Each item is loaded live and backed up before update; unchanged fields are preserved.
- Deletion Center supports bulk Trash, bulk Restore, and bulk deletion of unused media.
- Shared/referenced media is skipped automatically.
- Suggested Changes and Approval Queue support bulk Approve and Reject.

## Safety
- Destructive actions still use the existing Settings locks, previews, confirmation, backups, and post-operation synchronization.
- Permanent media deletion requires two confirmations and only processes media with zero synchronized references.
