# Architecture Decision Records

This directory stores Architecture Decision Records (ADRs) for decisions that materially affect structure, data, security, integration, execution safety, deployment, or operations.

## Required format
- Status: Proposed / Accepted / Superseded / Rejected.
- Date and decision owners.
- Context and constraints.
- Considered options.
- Decision.
- Consequences and risks.
- Migration or rollback plan.
- Related tracker tasks and commits.

## Initial accepted decisions
1. Desktop WPF on .NET 8.
2. SQLite with EF Core and code-controlled initialization.
3. Layered projects: Domain, Application, Infrastructure, Persistence, WordPress, AI, Automation, Reporting, Desktop, Tests.
4. Offline-first reads; remote synchronization and writes are explicit.
5. WordPress application passwords are protected locally and never logged.
6. AI results are proposals; production writes pass through review, approval, execution, verification, and evidence.
7. Background operations may update status but must not open persistent overlays automatically.
