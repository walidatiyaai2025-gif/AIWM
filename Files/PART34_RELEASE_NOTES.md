# Part 34 — Default Data Loading, Puter Response Hardening, and Smart Apply

## Changes

- All implemented screens now load their saved SQLite-backed data during application startup.
- AI Studio provider settings are preloaded during startup.
- Theme Inspector now opens in an offline-ready state and does not perform an automatic network request. Live discovery runs only when the user explicitly clicks Discover.
- Navigation page loads now use guarded loading so one module failure does not block the rest of the application.
- Puter/OpenAI-compatible response parsing now supports string, object, and array content shapes.
- Invalid or non-JSON AI responses now produce a clear provider error with a safe response excerpt instead of a low-level JSON token exception.
- Suggested Changes now includes a Smart Apply preview with the exact execution stages and expected verified value.
- Added Apply Safe Selected to execute only selected Low-risk, direct-action suggestions.
- Safe batch execution approves, backs up, sends to WordPress, reads back, verifies, and reports verified/failed/skipped counts.

## Startup behavior

SQLite data is loaded before the main window is displayed. No destructive action or automatic live WordPress mutation is performed.

## Verification

Run:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```
