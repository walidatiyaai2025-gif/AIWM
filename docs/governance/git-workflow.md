# Git Workflow and Pull Request Policy

## Branches
- `main`: protected, releasable, and buildable at all times.
- `feature/<task-id>-<name>`: product work.
- `fix/<task-id>-<name>`: defects.
- `docs/<task-id>-<name>`: documentation only.
- `release/<version>`: stabilization when needed.

## Commit rules
- One coherent purpose per commit.
- Reference tracker task numbers in commit messages when practical.
- Do not commit `bin`, `obj`, local databases, credentials, logs, generated backups, or secrets.

## Pull request gate
- Clear scope and affected tracker tasks.
- Build and tests reported.
- Screenshots for material UI changes.
- Data migration, security, and rollback impact documented.
- At least one reviewer for high-risk changes.
- No direct merge when the branch is known to fail compilation.

## Emergency fixes
Emergency fixes still require a follow-up review, build evidence, tracker update, and release note.
