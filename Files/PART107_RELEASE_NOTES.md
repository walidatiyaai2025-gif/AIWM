# Part 107 — Global Resource Scope Fix and XAML Validation Gate

## Fixed
- Moved `BoolToVisibility` from `MainWindow.Resources` to `Application.Resources`.
- This makes the converter available to standalone `UserControl` instances such as `VisualWordPressEditorView` during construction.
- Removed the duplicate window-scoped declaration from `MainWindow.xaml`.
- Prevents the startup `XamlParseException` that stopped the splash screen at 0%.

## Added
- `Build/Validate-XamlResources.ps1` scans all Desktop XAML files for unresolved `StaticResource` keys.
- The script exits with a failure code when a referenced key is missing.

## Validation
Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build\Validate-XamlResources.ps1
```

Then run the normal build gate.
