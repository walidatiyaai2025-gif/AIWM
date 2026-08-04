# Part 110 — Safe Bridge Gate & Dry-Run Validation

- Upgraded the bundled WordPress Bridge to version 1.2.0.
- Added an authenticated no-write endpoint: `POST /wp-json/aiwp-manager/v1/visual-css/validate`.
- Added a safe dry-run command in Visual WordPress Editor.
- Added a 15-minute execution gate: full diagnostics and dry-run validation must both be fresh and successful before WordPress execution.
- Added direct buttons to open the bundled plugin and the selected site's WordPress plugin upload page.
- Added managed CSS checksum and rule count to dry-run results.
- Added WordPress API logging for dry-run validation requests and responses.
- Updated offline Bridge validation to require version 1.2.0 and the new validation route.
- Rebuilt the bundled plugin ZIP and SHA256 checksum.
