# Phase 1 Part 17 — Safe WordPress Deletion Center

## Added

- Offline impact preview for posts, pages, and media.
- WordPress Trash support for posts and pages.
- Restore from Trash to draft, publish, pending, or private.
- Optional permanent content deletion with two confirmations.
- Optional permanent media deletion with two confirmations.
- Media reference analysis using source URLs, WordPress image classes, and attachment IDs.
- Automatic blocking when media is referenced by synchronized content.
- Content + exclusive-media workflow:
  - moves the post/page to Trash;
  - permanently deletes only media referenced exclusively by that content;
  - preserves shared media.
- WordPress JSON backup before content deletion.
- Media metadata and binary-file backup before media deletion.
- Local SQLite database backup before permanent operations when enabled.
- Automatic synchronization after successful operations.
- Execution-job records for trash, restore, and permanent deletion.
- Settings safety locks for destructive operations.

## Safety defaults

- Move to Trash: enabled.
- Permanent content deletion: disabled.
- Permanent media deletion: disabled.
- Backup before permanent deletion: enabled.

## Important media behavior

WordPress attachments generally do not use Trash. Deleting media through the REST API with `force=true` is permanent. The application therefore downloads the original file and stores metadata locally before sending the delete request. Media referenced by other synchronized posts or pages is blocked from deletion.

## No migration required

Safety settings use the existing `ApplicationSettings` key/value table. This part does not add new database tables.
