# Part 12 — Reliable Upsert Sync

This release fixes the SQLite unique-constraint failure raised during repeated WordPress synchronization.

## Fixes

- Deduplicates WordPress objects returned across adjacent REST pagination pages.
- Loads existing local content once and performs deterministic insert-or-update behavior.
- Prevents multiple Added EF Core entities from sharing `(SiteId, ContentType, WordPressId)`.
- Applies the same reliable upsert behavior to WordPress categories.
- Saves the complete snapshot inside a SQLite transaction.
- Rolls back and clears EF tracking when persistence fails.
- Logs inserted and updated counts without logging credentials or content bodies.

No new database migration is required.
