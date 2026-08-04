# Part 60 — Sidebar Search + Startup Resource Fix

## Fixed
- Replaced the missing `AccentBrush` reference in `MainWindow.xaml` with the existing `GoldOnLightBrush` dynamic resource.
- Prevents the startup `XamlParseException` reported at line 316.

## Added
- Live search box at the top of the sidebar.
- Arabic and English aliases for all major screens.
- Empty menu groups are hidden while filtering.
- Matching groups expand automatically.
- `Ctrl+K` focuses and selects the sidebar search box.
- `Esc` clears the search and releases keyboard focus.

## Documentation
- Updated the bundled Arabic Word user guide with Part 60 instructions and shortcuts.
