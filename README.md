> Current build: Part 66 — Accordion Navigation + Article Generator

# AI WordPress Website Manager — Phase 1 Part 19

This release builds on Part 18 and adds a global black/gold design, explicit offline-first loading, and reusable multi-select bulk operations.

## Build

```powershell
dotnet restore .\AIWordPressManager.sln
dotnet build .\AIWordPressManager.sln -c Debug
dotnet test .\AIWordPressManager.sln -c Debug --no-build
```

## Test checklist

1. Open the application and verify gold text, borders, selections, tabs, inputs, and buttons across all screens.
2. Disconnect the internet and open WordPress Explorer, Post SEO Editor, Deletion Center, Category Planner, Internal Links, and Suggested Changes. Existing SQLite data should remain available.
3. In Post SEO Editor use Ctrl/Shift to select several items, choose only the bulk fields you want, then run Preview and apply bulk update on test content.
4. In Deletion Center select several posts/pages and test Move selected to Trash, then Restore selected.
5. Select multiple media items. Referenced media must be skipped; only unused media can proceed through backup and permanent deletion confirmation.
6. In Suggested Changes or Approval Queue select multiple proposals and test bulk approval/rejection. These decisions remain local and do not execute WordPress changes.

No database migration is required for Part 19.


## Part 20 — Execution Center
Open **Execution Center** after approving concrete changes. Use Ctrl/Shift-click for bulk execution. Only supported low/medium-risk content field changes execute directly; all others remain blocked for manual/staging workflows.


## Release notes
All release and compile-fix notes are stored in the root `Files` folder.

## Part 86
This package includes the professional dynamic color system and AI Automation readiness validation. See `Files/PART86_RELEASE_NOTES.md`.

Part 135 adds protected global loading and locking for Approval/Execution workflows.
