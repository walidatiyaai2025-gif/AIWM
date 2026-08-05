# Risk Register

| ID | Risk | Impact | Probability | Owner | Response | Verification |
|---|---|---:|---:|---|---|---|
| R-01 | WordPress write changes unintended content | Critical | Medium | Execution owner | Approval, backup, scoped adapter, verify, evidence, rollback | Execution integration tests and evidence record |
| R-02 | Credentials exposed in SQLite or logs | Critical | Low | Security owner | Local protection, redaction, no secret logging | Database and log inspection |
| R-03 | Database upgrade corrupts local cache | High | Medium | Database owner | Transactional initialization, backup before destructive migration | Fresh/upgrade/restore tests |
| R-04 | Background refresh freezes or overlays UI | High | Medium | Desktop owner | Page-scoped operations, cancellation, no auto-dialogs | Multi-page soak test |
| R-05 | AI proposal is unsafe or inaccurate | High | High | AI owner | Provider trace, confidence/risk, human review, no direct writes | Approval workflow tests |
| R-06 | WordPress API rate limiting or timeout | Medium | High | Integration owner | Retry with backoff, cancellation, resumable sync | Fault-injection tests |
| R-07 | Duplicate sites or jobs violate unique constraints | High | Medium | Persistence owner | URL normalization, idempotency, lookup-before-insert | SQLite integration tests |
| R-08 | Arabic localization blocks startup | High | Medium | Localization owner | First-render-safe localization and bounded traversal | Arabic/English startup smoke tests |
| R-09 | Release artifact differs from tested build | High | Low | Release owner | Immutable versioned artifacts and checksums | Release checklist |
| R-10 | Tracker status diverges from actual code | Medium | Medium | Project owner | Evidence-based completion and dated notes | Periodic repository audit |
