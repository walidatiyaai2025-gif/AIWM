# Part 35 — AI Action Center and Puter Output Recovery

## Added
- New **AI Action Center** under the AI Actions navigation group.
- Loads its state from SQLite during application startup and whenever the selected site changes.
- Unified counters for safe actions, approval queue, manual/staging work, completed changes, and failed changes.
- Direct actions:
  - Apply all low-risk verifiable changes.
  - Retry failed direct actions.
  - Roll back the selected executed action.
  - Cancel a running action batch.
  - Open Suggested Changes or Execution Center.
- Progress percentage and current execution step.

## Puter reliability
- Puter requests now omit temperature and use `max_completion_tokens`.
- Increased Puter output budget for reasoning-capable models.
- Automatic one-time retry when the provider returns empty content with `finish_reason = length`.
- Clear user-facing message when the model consumes the output budget without producing visible JSON.

## Safety
- Action Center only auto-applies changes already classified as low risk and directly executable.
- Every execution still follows approval, backup, WordPress update, read-back verification, and audit history.
- High-risk and unsupported operations remain in manual/staging workflows.
