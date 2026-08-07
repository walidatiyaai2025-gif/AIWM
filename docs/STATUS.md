# Project Status

Last updated: 2026-08-07  
Current desktop version: 2.3.1  
Default branch: `main`  
Baseline commit: `12c0d2675c198078602c3d0f0cc58d063b45c594`

## Current milestone

The first controlled WordPress user journey is implemented and merged:

1. Dashboard
2. Sites
3. WordPress Explorer
4. SEO Audit
5. Suggested Changes
6. Approval Queue
7. Execution Center
8. Evidence Center

Execution receipts, build identity, support bundles, integrity verification, and Help diagnostics are included in the 2.3.1 baseline.

## Validation baseline

The merge baseline passed:

- PR Fast Validation
- Stability Build
- Windows Desktop Build
- Verify Windows Solution
- Release restore/build
- Non-UI tests
- Desktop startup smoke test
- First-journey contract validations

## Release readiness

### Completed

- Release build and Setup publishing compile successfully.
- `Build-And-Run.bat` targets `main`.
- Build version, source branch, and source commit are embedded.
- Terminal executions produce HTML and JSON receipts.
- Recent receipts are restored and exposed in Evidence Center.
- Support bundles redact known secrets and include SHA-256 verification.
- Windows CI produces JSON and Markdown startup acceptance evidence with executable SHA-256 and build identity; artifacts are retained for 14 days.
- A release-candidate validation and operator sign-off checklist is available in `docs/RELEASE_CANDIDATE_CHECKLIST.md`.

### Still requires operator acceptance

- Install the generated Setup package on a clean Windows machine.
- Complete the full journey against a disposable WordPress site.
- Verify successful, failed, cancelled, and partially failed executions.
- Confirm before/after evidence and rollback behavior using real WordPress data.
- Review Arabic RTL and English LTR presentation across the complete journey.
- Inspect a generated support bundle before sharing it outside the organization.

## Working rules

- Do not commit directly to `main`.
- Start each change from the latest successful `main`.
- Keep pull requests focused and leave them Draft until required CI is green.
- Update this file whenever a milestone or release-readiness condition changes.
- Do not report a runtime scenario as complete based only on token-contract scripts.
