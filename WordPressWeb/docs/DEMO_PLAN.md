# AIWM WordPress Web Edition — Demo Closure Plan

Status: **IN PROGRESS**
Authority: GitHub Issue #11
Branch: `variant/wordpress-web-demo`

## Mission

Produce the first complete, installable WordPress-hosted AIWM demo with functional and visual parity to the current desktop journey. No partial slice may be reported as complete product delivery.

## Architecture

### Host
- WordPress plugin, PHP 8.1+.
- Runs inside `wp-admin`; no separate Node/PHP daemon required in production.
- React + TypeScript SPA build output packaged into the plugin.

### Application boundary
- REST namespace: `aiwm/v1`.
- Capability protected routes.
- WordPress REST nonce for browser session requests.
- Mutating endpoints require explicit permission callbacks and validation.

### Persistence
Custom AIWM tables for sites, audits, recommendations, jobs, executions and evidence. Additional tables are added only when required by a real feature path.

### Long-running work
Audits, AI provider calls, bulk changes, verification and remote-site operations must be batched, checkpointed, resumable and idempotent. Web requests enqueue or advance bounded work; they do not hold a PHP request open for a full large-site operation.

## UI parity target

The Web Edition must preserve the desktop product's information architecture and product identity rather than fall back to generic WordPress settings-table UI.

Parity dimensions:
- left navigation hierarchy;
- page titles, density and card structure;
- status semantics and progress feedback;
- approval/execution/recovery dialogs;
- before/after review presentation;
- evidence/receipt visibility;
- Arabic RTL and English LTR;
- desktop-like spacing and restrained responsive behavior.

A web-specific implementation is allowed where browser behavior requires it, but visual hierarchy and interaction intent must remain recognizably AIWM.

## Performance gates

1. Dashboard bootstrap uses bounded aggregate queries; no full-table payloads.
2. Large lists use server-side pagination/filtering.
3. Explorer content is lazy-loaded by resource type/page.
4. Audit work is divided into bounded batches with persisted cursors.
5. AI calls never block navigation state and expose queued/running/failed status.
6. Mutations have idempotency keys and duplicate-execution protection.
7. Read-heavy summaries may be cached only with explicit invalidation rules.
8. Browser assets are route-split/minified for the release build.
9. API responses exclude secrets and unnecessary payload fields.
10. Demo evidence records representative endpoint/job timings and identifies any remaining hotspot.

## Functional closure sequence

### Phase 1 — Runtime foundation
- [x] Variant branch created.
- [x] P0 acceptance issue created.
- [x] Plugin activation/schema foundation.
- [x] Capability-gated wp-admin application host.
- [x] Health and live dashboard REST endpoints.
- [x] Initial AIWM visual shell.
- [ ] Build pipeline that emits an installable plugin ZIP.

### Phase 2 — Sites + Explorer
- [ ] Add/edit/remove managed site.
- [ ] Secure credential reference/storage strategy.
- [ ] Verify connection.
- [ ] Active-site selection.
- [ ] Real content/taxonomy/media Explorer with pagination.

### Phase 3 — Audit + Suggestions
- [ ] Queue batched SEO audit.
- [ ] Persist findings and score inputs.
- [ ] Generate real proposed changes from provider/service path.
- [ ] Before/after and risk presentation.

### Phase 4 — Approval + Execution
- [ ] Persist approval/rejection decisions.
- [ ] Site identity re-check immediately before execution.
- [ ] Backup/evidence requirement for supported mutations.
- [ ] Idempotent queued execution.
- [ ] Completed/failed/cancelled/partial-failure semantics.
- [ ] Recovery/retry/rollback UX.

### Phase 5 — Evidence + AI Providers
- [ ] Persist and list evidence/receipts.
- [ ] Integrity hashes where appropriate.
- [ ] Provider abstraction.
- [ ] Gemini/OpenAI configuration path with server-side credentials.
- [ ] Provider health/test action with safe error reporting.

### Phase 6 — Remote target demo
- [ ] Disposable WordPress target connected.
- [ ] Connector capability defined where native REST is insufficient.
- [ ] Full demo journey exercised against real target data.

### Phase 7 — UX/performance/release demo
- [ ] Desktop visual parity review route by route.
- [ ] Arabic RTL acceptance.
- [ ] English LTR acceptance.
- [ ] Keyboard/focus/responsive checks.
- [ ] Performance profiling and bounded-job validation.
- [ ] Clean plugin activation test.
- [ ] Installable ZIP generated.
- [ ] End-to-end demo evidence recorded.

## Demo-ready rule

The only valid terminal status is `DEMO-READY`, and it requires all acceptance gates in Issue #11 plus a reproducible installable ZIP from the same candidate head. Until then, status remains `IN PROGRESS` with explicit gaps.