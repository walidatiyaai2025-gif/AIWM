# Part 98 — Live Execution Pipeline and Verified Evidence

## Added

- Live execution pipeline inside Execution Center.
- Per-step states for job creation, backup, evidence, WordPress read/write, verification, and final logging.
- Before/after screenshot tabs using the existing Playwright visual inspection service.
- Direct buttons to open captured evidence files.
- Evidence capture controlled by Settings → AI Automation.
- After evidence is captured only after at least one verified WordPress write.
- Selected queue rows now show an exact preview pipeline before execution.

## Safety

- Unsupported, high-risk, and staging-required actions remain blocked.
- Screenshot failures do not falsely mark a WordPress operation as successful.
- WordPress success still requires a read-back verification.
- Cancellation and failures update the visible pipeline state.

## Current direct WordPress adapters

- SetTitle
- SetSlug
- SetExcerpt
- SetStatus
- SetContent

Visual, theme, media, and builder routes are identified by the AI router but remain blocked until their safe adapters are implemented.
