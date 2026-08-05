# AI WordPress Manager — Product Scope

## Product
AI WordPress Manager is a desktop WPF application on .NET 8 for managing multiple authorized WordPress websites through the WordPress REST API, with an offline-first SQLite/EF Core data layer and guarded AI-assisted workflows.

## Version 1.0 scope
- Register, validate, select, edit, and remove WordPress site connections.
- Synchronize posts, pages, media, taxonomy, comments, users, and site metadata into SQLite.
- Run SEO, content, link, visual, performance, accessibility, and configuration audits.
- Generate AI-assisted proposals without writing directly to WordPress.
- Review, approve, execute, verify, evidence, retry, and rollback supported changes.
- Provide backups, restore, settings, logs, reports, help, Arabic RTL, English LTR, light/dark themes, and role-based access.

## Out of scope for 1.0
- Hosting WordPress websites.
- Bypassing WordPress permissions or security controls.
- Automatic destructive changes without review, backup, and verification.
- Storing WordPress application passwords as plain text.

## Product invariants
- SQLite loads first; remote synchronization runs explicitly or in controlled background jobs.
- AI output is a proposal until it enters the approval workflow.
- WordPress writes require an execution plan, risk classification, backup policy, and verification evidence.
- Every critical error must be copyable and correlated.
