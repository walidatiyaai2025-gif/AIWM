# Part 27 — AI Suggestion Actions and Build Repair

- Fixed the Puter WPF namespace collision by explicitly using `System.Windows.Application.Current`.
- Added visible AI provider, confidence, exact AI proposal, and clean explanation for each suggestion.
- Added **Apply** and **Why?** actions beside every suggested change.
- Added direct single-item workflow: confirm → approve → SQLite backup → WordPress update → re-read verification.
- Direct execution is limited to supported low/medium-risk content fields; high-risk and staging changes remain blocked.
- Added a mandatory SQLite safety backup before each execution/rollback batch.
- AI output is tagged with the provider that generated it; local fallback is shown as `Rules Engine`.
