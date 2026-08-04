# Part 16 — Approval Workflow and Runtime Sync Settings

- Added persistent SuggestedChanges with a new EF Core migration.
- Generates proposals from local SEO, content, broken-link and category data.
- Added Suggested Changes and Approval Queue screens.
- Approval and rejection are local decisions only; no WordPress execution occurs.
- Added SQLite-backed synchronization settings UI.
- Background synchronization rereads the interval after every cycle.
