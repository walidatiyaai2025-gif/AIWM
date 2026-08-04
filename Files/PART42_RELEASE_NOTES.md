# Part 42 — Compile Fix + AI Site Brain

- Fixed missing `AIWordPressManager.Application.Abstractions` import in `VisualInspectorViewModel`.
- Added persistent per-site AI memory using SQLite `ApplicationSettings` (no schema migration required).
- Added AI Site Brain screen with language, tone, audience, SEO plugin, page builder, brand colors, image size, linking strategy, category strategy, content/design rules, and rejected patterns.
- Added startup/offline loading and site-switch reload for AI Site Brain.
- Added status-bar progress and save/reload commands.
