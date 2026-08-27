# Worker 4 — Functional Journey / AI / Remote WordPress / E2E

Authority: Issue #11  
Integration base: PR #12 / `variant/wordpress-web-demo`  
Worker branch: `worker/wpweb-e2e-journey`

## Scope implemented

This slice owns the real functional composition behind the Web Edition journey without claiming final demo readiness:

1. Managed-site creation using a **server-side credential reference** only.
2. Remote WordPress identity/authentication verification through native WordPress REST APIs.
3. Bounded posts/pages synchronization and Explorer reads.
4. Persisted SEO audit with scores derived only from real content checks.
5. Gemini provider call with server-side key resolution and explicit provider failure semantics.
6. Persisted Suggested Changes; AI never writes to WordPress.
7. Explicit human approve/reject decision.
8. Before snapshot/evidence before any supported mutation.
9. Governed post/page `title`, `excerpt`, or `slug` mutation through native WordPress REST.
10. Remote re-read verification, persisted execution receipt, and SHA-256 evidence records.
11. Persistent jobs with queued/running/completed/failed/cancelled semantics.
12. Retry/idempotency protection: an execution uses `execute:recommendation:<id>` and a completed mutation is reused instead of issued again.

No AIWM Connector plugin is required for this current demo mutation set because WordPress core REST + Application Passwords supports authenticated post/page reads and edits. A connector remains appropriate later only for capabilities that core REST cannot expose safely.

## Server-side secrets

Do not send a Gemini key or remote WordPress Application Password from browser JavaScript.

Configure the host WordPress server, preferably in `wp-config.php` or through Worker 3's secure credential resolver:

```php
define('AIWM_GEMINI_API_KEY', 'server-secret');
define('AIWM_GEMINI_MODEL', 'gemini-2.5-flash');
define('AIWM_REMOTE_CREDENTIALS', [
    'demo-target' => [
        'username' => 'aiwm-demo-admin',
        'application_password' => 'xxxx xxxx xxxx xxxx xxxx xxxx',
    ],
]);
```

Integration extension points:

- `aiwm_web_provider_api_key` — Worker 3 or another secure secret service may supply the provider key.
- `aiwm_web_remote_credential` — secure credential store may resolve a `credential_ref` without changing Worker 4 routes.
- `aiwm_web_ai_provider` — future OpenAI/other provider implementations may replace the Gemini adapter while preserving the governed recommendation contract.

Remote targets require HTTPS. For a disposable local-only test environment, HTTP can be explicitly enabled with `AIWM_ALLOW_INSECURE_REMOTE=true`; never use that override for production targets.

## Real SEO score inputs

The bounded demo audit reads up to 10 posts and 10 pages per run and checks five persisted signals per object:

- title length 20–70 characters;
- non-empty excerpt;
- at least 120 content words;
- non-empty slug no longer than 75 characters;
- at most one `<h1>` inside post/page content.

`score = passed_checks / total_checks * 100`. The audit row stores the score and findings count; its job payload stores `score_inputs` and the actual findings used by the AI recommendation stage. No static score or sample finding is presented as live state.

## Live E2E acceptance

The harness is `WordPressWeb/tests/e2e-live.sh`. It requires two real WordPress sites:

- AIWM Web Edition host with this plugin active;
- disposable remote target with at least one deliberately imperfect post/page and a WordPress Application Password.

The host itself is authenticated by a WordPress Application Password so the script exercises the same capability-protected REST routes as the Web UI.

Required environment variables:

```bash
export AIWM_HOST='https://host.example.test'
export AIWM_HOST_USER='host-admin'
export AIWM_HOST_APP_PASSWORD='xxxx xxxx xxxx xxxx xxxx xxxx'
export AIWM_TARGET_URL='https://target.example.test'
export AIWM_TARGET_CREDENTIAL_REF='demo-target'
export AIWM_TARGET_NAME='Disposable WordPress E2E'
```

Run:

```bash
bash WordPressWeb/tests/e2e-live.sh | tee worker4-e2e-receipt.json
```

The harness fails closed unless it proves:

- connect + identity/auth verification;
- sync + Explorer on real target content;
- derived SEO audit score/findings;
- real Gemini recommendation persisted as a Suggested Change;
- persisted human approval;
- before evidence → mutation → remote verification → receipt/evidence;
- duplicate execution request returns the same completed job;
- queued cancel never reports success, then explicit retry can complete;
- a deliberately missing credential reference surfaces a controlled remote failure;
- a later independent REST request can still read the completed job, proving persisted state across page/request restart.

## Provider failure contract

Gemini failures are normalized without exposing secrets:

- missing key → `aiwm_ai_missing_key`;
- invalid request/model → `aiwm_ai_invalid_request`;
- invalid/restricted key → `aiwm_ai_invalid_key`;
- quota/rate limit → `aiwm_ai_rate_limited`;
- timeout → `aiwm_ai_timeout`;
- provider unavailable → `aiwm_ai_unavailable`;
- malformed provider output → `aiwm_ai_malformed_response` or `aiwm_ai_no_valid_suggestions`.

AI output is constrained to an existing audited object + one of `title`, `excerpt`, `slug`. Unknown object IDs or fields are discarded. The provider never receives authority to call the WordPress mutation path directly.

## Acceptance evidence to record

For the Integration Lead's final run, retain:

- target WordPress version from the target REST index / environment;
- exact Worker 4 candidate SHA and final integrated plugin SHA;
- Gemini model used;
- site/content IDs used in Explorer/audit;
- audit score and findings count;
- Suggested Change ID + before/proposed values;
- approval state;
- execution ID + verification object;
- before/after/receipt evidence SHA-256 values;
- controlled failure response;
- cancelled/retried job IDs;
- the final JSON receipt printed by `e2e-live.sh`.

This worker may return `READY-FOR-INTEGRATION` only after code/contract validation. It must return `BLOCKED` for live acceptance if a disposable target, host deployment, or Gemini credential is unavailable. Only the Integration Lead may declare `DEMO-READY` after the final ZIP is built and accepted from the integrated head.
