# Part 100 — System-wide Contextual Help Mode

## Added

- Global `?` Help Mode available from the Office ribbon title bar.
- `F1` toggles contextual Help Mode.
- `Shift+F1` continues to open the full user guide.
- Hover guidance for interactive controls across every screen.
- Clicking a control while Help Mode is active opens a full instruction window and prevents the underlying command from executing.
- Automatic help generation for buttons, menu items, grids, text fields, password fields, combo boxes, check boxes, radio buttons, sliders, tabs, lists, and trees.
- Screen-specific instructions for all major navigation destinations.
- Detailed execution safety descriptions for Approve, Execute, Retry, Rollback, Generate, Delete, Backup, Refresh, Copy, Export, Settings, and AI actions.
- High-visibility Help Mode banner and help cursor.
- Extended tooltip display duration and readable professional tooltip layout.

## Safety behavior

Help Mode is inspection-only. Clicking a control in Help Mode displays instructions and does not execute the action. Press `F1` or click `? ON` to return to normal operation.

## Files

- `src/AIWordPressManager.Desktop/Services/ContextualHelpService.cs`
- `src/AIWordPressManager.Desktop/ContextHelpWindow.cs`
- `src/AIWordPressManager.Desktop/MainWindow.xaml`
- `src/AIWordPressManager.Desktop/MainWindow.xaml.cs`
- `src/AIWordPressManager.Desktop/Themes/Theme.xaml`
