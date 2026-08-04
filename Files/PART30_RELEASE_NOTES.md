# Part 30 — Active Navigation + Startup SQLite Hydration

## Implemented

- Sidebar navigation now highlights the currently open screen with:
  - gold active marker,
  - selected background,
  - brighter gold text,
  - bold label.
- Added `NavigationSelectionConverter` and navigation tags for all sidebar destinations.
- Application startup now loads saved SQLite data before showing the main window.
- The first saved site is selected automatically when no site was already selected.
- Site-specific offline data is hydrated when the selected site changes.
- Startup hydration covers:
  - Sites and selected site details,
  - Settings,
  - WordPress Explorer snapshot,
  - Content Audit saved results,
  - SEO Audit saved results,
  - Broken Links saved results,
  - Category Planner,
  - Internal Links,
  - Suggested Changes,
  - Deletion Center,
  - Post SEO Editor offline data,
  - Execution Center.
- Added database-only loading methods for saved Content Audit, SEO Audit, and Broken Link results.
- Status bar now displays live database/data-loading state.

## Safety

- Startup hydration does not call WordPress synchronization.
- Broken-link startup loading reads stored results only and does not perform HTTP scans.
- Theme discovery is not executed automatically because it may require a live WordPress request.

## Validation performed here

- MainWindow.xaml XML syntax validated.
- Theme.xaml XML syntax validated.
- App.xaml XML syntax validated.
- No absolute Part1 project paths found in `.sln` or `.csproj` files.

## Build note

The .NET SDK is not installed in the packaging environment, so run `dotnet build` and `dotnet test` on the development machine.
