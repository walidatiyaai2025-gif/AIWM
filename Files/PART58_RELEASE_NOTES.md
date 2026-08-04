# Part 58 - Database Backup and Restore

## Compile fix
- Added the missing `System.ComponentModel` namespace required by `ICollectionView` in `ContentAuditViewModel`.

## Database recovery
- Added verified database restore from the Backups screen.
- Restore can use a selected recorded backup or an external `.db`, `.sqlite`, or `.sqlite3` file.
- Every restore source is checked with `PRAGMA integrity_check`.
- A safety backup of the current database is always created before restore.
- Restore is staged outside the running process, removes stale WAL/SHM/journal files, replaces the database, and restarts the application.
- Added restore progress, confirmation, and detailed error popups.

## Documentation
- Updated the in-app Arabic Word user guide to Part 58 with full backup and restore instructions.
