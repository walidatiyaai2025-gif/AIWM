# Roadmap

This roadmap starts from the merged 2.3.1 first-journey baseline.

## Milestone 1 — Windows acceptance and release candidate

Goal: prove that the compiled application and Setup package work on a clean Windows environment.

- Install, launch, sign in, and exit cleanly.
- Validate SQLite initialization and upgrade behavior.
- Test the complete first journey with a disposable WordPress site.
- Validate receipt persistence across application restarts.
- Validate support bundle creation, redaction, integrity, and compatibility.
- Record acceptance evidence and publish a release candidate checklist.

Exit condition: all acceptance checks pass with no critical startup, data-loss, credential, or execution defect.

## Milestone 2 — Execution safety hardening

Goal: make every WordPress mutation traceable and recoverable.

- Validate site identity immediately before execution.
- Require backup/evidence for every supported mutation route.
- Add runtime tests for completed, partially failed, failed, and cancelled jobs.
- Verify idempotency and duplicate-execution protection.
- Test rollback behavior and surface unsupported rollback cases clearly.

Exit condition: supported mutations have verified backup, receipt, evidence, and recovery behavior.

## Milestone 3 — Support privacy hardening

Goal: make diagnostic bundles safe and predictable for support use.

- Add executable redaction tests using representative credential formats.
- Cover connection strings, cookies, query-string secrets, JWTs, XML values, and multiline values.
- Warn users that machine, user, path, site, and execution metadata may be included.
- Distinguish accidental-corruption checking from cryptographic authenticity.
- Add bundle size limits and receipt retention controls.

Exit condition: support bundles pass automated privacy tests and expose clear sharing guidance.

## Milestone 4 — Localization and accessibility acceptance

Goal: complete Arabic/English runtime acceptance for the first journey.

- Verify Arabic RTL and English LTR navigation.
- Validate theme contrast and sidebar readability in every palette.
- Test keyboard navigation, focus order, shortcuts, and screen scaling.
- Localize dynamically injected journey and support panels.
- Remove clipped or untranslated text.

Exit condition: the complete journey is usable in both languages at supported display scales.

## Milestone 5 — Operational release

Goal: publish a supportable 2.3.x desktop release.

- Produce signed or checksummed release artifacts.
- Publish Setup and release notes.
- Document upgrade, backup, rollback, and support-bundle procedures.
- Confirm `main` CI and release artifact retention.
- Tag the validated release commit.

Exit condition: a reproducible release is available with installation and support documentation.
