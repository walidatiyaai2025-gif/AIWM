# Part 99 — Compile Fix and Self-Healing Retry

## Fixed
- Added the missing `System.IO` namespace required by `File.Exists` in `ExecutionCenterViewModel`.
- Resolves `CS0103: The name 'File' does not exist in the current context`.

## Added
- Added one controlled automatic retry for transient WordPress execution failures.
- Retry applies to timeout, connection reset/refused, HTTP 429, 502, 503, and 504-style failures.
- Permanent validation, authentication, permission, unsupported-adapter, and high-risk failures are not retried.
- The retry is visible inside the Live Pipeline as `Self-healing retry`.
- Cancellation remains respected during the retry delay.

## Safety
- Maximum attempts: 2 total.
- Delay before retry: 2 seconds.
- Existing job failure pause/circuit-breaker settings remain active.
