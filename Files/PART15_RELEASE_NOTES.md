# Phase 1 Part 15 — Offline First + Planning

## Offline-first behavior
- WordPress Explorer opens from SQLite immediately.
- Manual Synchronize now updates the local cache.
- Failure of online synchronization leaves the offline cache usable.
- A hosted background service synchronizes connected sites every 60 minutes by default.
- Interval and startup behavior are configurable in `appsettings.json`.

## Step 1: Category Planner
- Reads categories only from SQLite.
- Classifies empty, weak, and healthy categories.
- Produces recommendations and risk labels.
- Does not delete, merge, rename, or reassign WordPress content.

## Step 2: Internal Links
- Reads published posts/pages only from SQLite.
- Generates candidate source/target pairs using shared topical terms.
- Skips links already present in source HTML.
- Does not write links to WordPress.

## Configuration
```json
"Synchronization": {
  "IntervalMinutes": 60,
  "RunOnStartup": true,
  "OfflineFirst": true
}
```
Minimum accepted interval is five minutes.
