# Part 111 — Managed Visual CSS History and Selective Recovery

## Delivered

- Upgraded the bundled WordPress Bridge to 1.3.0.
- Added authenticated Visual CSS history endpoints.
- Added a persistent, bounded execution history in WordPress (latest 100 changes).
- Added a Managed changes tab to Visual WordPress Editor.
- Added history refresh, status, checksum, active rule count, selector, CSS, page, theme, user, and timestamps.
- Added selective rollback of an active history item without exposing rollback tokens to the desktop client.
- Added WordPress API logging for history reads and history rollback requests.
- Extended full bridge diagnostics to validate history and history-rollback routes.
- Updated packaging and validation scripts for Bridge 1.3.0.

## Safety behavior

- History endpoints require an authenticated WordPress user with `edit_theme_options`.
- The history list never returns the previous full CSS revision or any rollback secret.
- A selective rollback is performed server-side by change ID.
- Rollback restores the complete Custom CSS revision that existed immediately before the selected change. Later changes may therefore also be removed, and the UI warns the operator before use.
- History storage is capped at 100 records to avoid unbounded WordPress option growth.

## Validation completed in the packaging environment

- All Desktop XAML files parsed successfully as XML.
- Modified C# files have balanced braces and parentheses.
- WordPress Bridge PHP syntax passed `php -l`.
- Source plugin and ZIP plugin SHA256 hashes match.
- Full .NET build was not run because the packaging environment does not contain the .NET SDK.
