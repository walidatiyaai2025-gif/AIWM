# Part 101 — AI Site Brain Foundation + Compile Fix + WordPress Bridge

## Compile corrections

- Fully qualified `System.Windows.Application.Current` in `ContextHelpWindow.cs` to prevent the `AIWordPressManager.Application.Current` namespace collision (CS0234).
- Reordered `ListView` before `ListBox` in the contextual-help pattern switch because `ListView` derives from `ListBox` (CS8510).

## AI Site Brain foundation

The site-specific memory profile now stores:

- Primary AI goal.
- Target keywords.
- Competitors.
- Publishing schedule.
- Per-site autopilot preference.

The Site Brain screen now displays:

- Brain readiness percentage/status.
- A generated "Today's AI Mission" summary.
- Goal and keyword context controls.
- An autopilot switch that remains subject to the existing safety, backup, evidence, approval, and verification policies.

Existing JSON profiles remain compatible because the new fields have safe defaults.

## WordPress plugin package

Added `WordPressPlugins/AIWordPressManager-Bridge-1.0.0.zip` and installation documentation.

The plugin is optional for core WordPress REST execution. Core title, slug, excerpt, status, and content operations continue to use the standard WordPress REST API and Application Passwords. The bridge provides a protected capability and health endpoint for advanced adapters and plugin/page-builder detection.
