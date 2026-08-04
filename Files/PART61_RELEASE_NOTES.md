# Part 61 — Performance Optimization + Command Palette

## Performance
- Replaced strong localization dictionaries with `ConditionalWeakTable` so unloaded controls are not retained.
- Replaced static strong DataGrid state collections with weak-key tables.
- Added 280 ms search debounce to paged grids.
- Reduced pagination allocations by removing the duplicated full source list.
- Cached searchable reflection metadata per row type.
- Added a memory command that releases hidden grid page caches.

## Command Palette
- Press `Ctrl + Shift + P`.
- Search screens and commands in Arabic or English.
- Navigate directly without opening sidebar groups.
- Includes a command to release hidden grid caches.
