# AIWM Worker Focus Lock

Effective immediately and until Issue #11 reaches `DEMO-READY`:

- The WordPress Web Edition demo is the sole active AIWM implementation priority.
- Do not start unrelated AIWM features, refactors, cleanup, desktop roadmap expansion, or opportunistic architecture work.
- Do not create parallel broad workers with overlapping ownership.
- Any worker assigned to AIWM must contribute directly to Issue #11 and the candidate branch/PR lineage.
- Existing desktop `main` remains the visual/functional reference baseline and must not be rewritten merely to simplify the Web Edition.
- Parallelization, if later used, must be narrow and non-overlapping (for example UI parity vs runtime/persistence) and must converge into the same demo candidate.
- No worker may report the product as complete until the installable WordPress plugin ZIP passes the complete demo acceptance gate.

Canonical execution branch: `variant/wordpress-web-demo`
Canonical P0 issue: #11
Canonical draft integration PR: #12

Terminal status before full acceptance: `IN PROGRESS`.
Terminal status after full acceptance only: `DEMO-READY`.
