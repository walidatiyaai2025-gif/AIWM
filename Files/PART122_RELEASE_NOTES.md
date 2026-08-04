# Part 122 — Startup Resource Fix and Resource Contract Gate

## Fixed
- Replaced missing `CardButtonStyle` with the existing global `NavigationCardButtonStyle` in AI Operations Center cards.
- Replaced the undefined `BackgroundBrush` with `AppBackgroundBrush`.
- Replaced the undefined `TextMutedBrush` with `TextSecondaryBrush`.
- Updated `ContextHelpWindow` to resolve `AppBackgroundBrush`, preventing startup failures in runtime-created help windows.

## Validation hardening
`Build/Validate-XamlResources.ps1` now validates:
- `StaticResource` references.
- `DynamicResource` references.
- C# `FindResource("...")` and `TryFindResource("...")` lookups.

The script fails before build/package when a global resource key is missing.

## Verification performed
- Parsed all Desktop XAML files successfully.
- Scanned all XAML and runtime resource references.
- Confirmed zero unresolved resource keys in the Desktop project.
