# Release Versioning Policy

AI WordPress Manager uses Semantic Versioning: `MAJOR.MINOR.PATCH`.

- MAJOR: incompatible database, workflow, API, or configuration behavior.
- MINOR: backward-compatible functionality.
- PATCH: backward-compatible defect, stability, documentation, or security fix.

## Version sources
The Desktop project version, assembly version, file version, informational version, UI footer, release notes, installer, and published artifact must identify the same release.

## Pre-release labels
Use `-alpha.N`, `-beta.N`, and `-rc.N` only for packages not approved for production.

## Release gate
A version is published only after successful restore/build/tests, database upgrade check, backup/restore smoke test, startup smoke test, and tracker/release-note update.
