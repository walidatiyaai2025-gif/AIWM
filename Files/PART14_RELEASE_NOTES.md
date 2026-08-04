# Phase 1 Part 14 — SEO Audit and Broken Links

## Added
- Local measurable SEO audit persisted to SQLite.
- Broken-link scanner with bounded concurrency, timeout handling, cancellation, HEAD/GET fallback, and local history table.
- New migration `20260802203000_AddSeoAuditAndBrokenLinks`.
- New production UI pages for SEO Audit and Broken Links.

## Safety
- WordPress remains read-only.
- The scanner checks at most 200 unique HTTP/HTTPS URLs per run.
- Credentials and Authorization headers are never written to scan results.
