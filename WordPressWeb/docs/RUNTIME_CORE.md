# AIWM WordPress Web Edition — Runtime Core

Status: **READY-FOR-INTEGRATION** for Worker 3 scope only. This is **not** a `DEMO-READY` claim.
Authority: Issue #11
Worker branch: `worker/wpweb-runtime-core`
Integration target: `variant/wordpress-web-demo` / PR #12

## Runtime contract

Worker 3 owns the server-side destination behind the Web Edition: lifecycle/schema, persistence, REST authorization, background jobs, idempotency/cancellation, evidence/receipts, credential storage boundary, caching and performance/audit instrumentation.

## Persistence

Schema version: `3`.

Custom tables use the active WordPress table prefix and the `aiwm_` suffix namespace:

- `sites`
- `site_state`
- `explorer_snapshots`
- `seo_audits`
- `findings`
- `suggested_changes`
- `approval_decisions`
- `jobs`
- `job_items`
- `executions`
- `evidence`
- `receipts`
- `ai_provider_config`
- `ai_usage`
- `activity_log`

Indexes are defined around site/status/time, audit/finding access, job state, idempotency keys, receipt UUIDs and other bounded list paths. The migration routine preserves legacy audit/recommendation rows into the new domain tables where those legacy tables exist.

Dashboard summaries are cached for a short bounded interval using WordPress object cache/transients and are explicitly invalidated by relevant writes.

## REST boundary

Namespace: `aiwm/v1`.

Implemented bounded routes cover:

- health and dashboard summaries;
- sites CRUD;
- Explorer snapshot lists;
- audits and findings;
- suggested changes and persisted approval decisions;
- execution creation/listing;
- job progress and cancellation;
- evidence and receipts;
- AI provider configuration metadata;
- activity/performance history.

List endpoints use server-side pagination with a hard maximum page size of 100 and selective fields.

All REST routes have explicit permission callbacks. Mutating routes require an authenticated user with `manage_aiwm` and a valid WordPress REST nonce (`X-WP-Nonce`). Input IDs, keys, text and URLs are validated/sanitized at the route boundary.

## Credential boundary

Provider/site secrets never appear in browser bootstrap responses or list endpoints.

Secrets are persisted behind opaque references. The stored value is encrypted with AES-256-GCM when OpenSSL is available, using WordPress salt-derived key material. Storage fails closed if the encryption primitive is unavailable. Secret options are non-autoloaded.

## Background jobs

The job engine prefers Action Scheduler when available and otherwise schedules bounded slices through WP-Cron.

Each job persists:

- unique idempotency key;
- current/total progress;
- cursor/checkpoint state;
- attempt/max-attempt counters;
- lock token and heartbeat timestamps;
- retry time;
- cancellation request time;
- terminal error/status.

Default slice contract is at most 25 logical items with an 8-second adapter budget. Retries use bounded exponential backoff. Stale job locks can be reclaimed after two minutes.

Additional work types integrate through `aiwm_web_process_job_slice`. Missing adapters fail explicitly rather than simulating success.

## Execution safety

Before an execution can be queued, runtime requires:

1. the suggested change to exist;
2. a current persisted approval whose version hash still matches the proposed change;
3. a verified target site with a persisted identity fingerprint;
4. captured before-state;
5. a caller-provided idempotency key.

Immediately before mutation, the execution job rechecks target identity, approval/version and before-state. Actual remote mutation is delegated to `aiwm_web_execute_change`, which is intentionally owned by the Worker 4 remote/integration path.

Terminal execution states are constrained to `completed`, `failed`, `partially_failed` and `cancelled`. Before-state evidence, result/terminal evidence and hash-backed receipts are persisted. Retry exhaustion and pre-mutation cancellation also close the execution record rather than leaving it indefinitely queued.

## Performance and audit evidence

REST dispatch and background job slice durations are recorded into `activity_log`. REST responses also expose `X-AIWM-Duration-Ms` for measured AIWM routes. The dashboard is cached with explicit invalidation, and list payloads are bounded/paginated.

## Security review notes

The runtime boundary specifically addresses capability enforcement, nonce misuse, direct REST access, SQL parameterization for request-controlled values, server-only credentials, opaque secret references, idempotent execution, stale-approval prevention and escaped/JSON-only evidence transport contracts. No runtime file-path input or PHP serialized user input is accepted by these endpoints.

## Integration hooks / known gaps

Worker 4 must provide and prove the real remote-site identity verification/sync and `aiwm_web_execute_change` mutation adapter. Explorer/audit producers must populate snapshots/findings through their real integration path. Provider health/call adapters must consume the protected credential reference server-side and record usage metadata. A real disposable WordPress environment is still required for activation/migration, REST authorization/nonce, queue and remote mutation integration tests.
