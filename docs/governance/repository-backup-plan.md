# Repository and Documentation Backup Plan

## Protected assets
- Git repository, branches, tags, releases, issues, and pull requests.
- Architecture, governance, user guides, release notes, and execution tracker.
- Release artifacts, checksums, migration notes, and recovery instructions.

## Strategy
1. GitHub is the primary remote source of truth.
2. Maintain at least one independent scheduled mirror or encrypted archive outside the primary GitHub account.
3. Include repository history, tags, Git LFS objects if introduced, release assets, and documentation.
4. Keep daily incremental and weekly full copies; retain monthly release baselines.
5. Do not include local SQLite databases, credentials, API keys, logs, or user backups in the source archive.

## Recovery test
Quarterly, restore the archive to an empty location, verify commit/tag history, run restore/build/tests, open documentation, and record the result.

## Ownership
- DevOps owner: archive generation and retention.
- Project owner: documentation completeness.
- Release owner: release artifact and checksum retention.
