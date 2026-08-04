# Part 33 — Provider Profiles and AI Studio

## Implemented

- Fixed Puter HTTP 400 errors caused by sending a non-default `temperature` value.
  - OpenAI-compatible request generation now supports provider-specific optional parameters.
  - Puter omits `temperature` completely and lets the routed model use its supported default.
- Added provider capability descriptions in Settings.
- Added a new **AI Studio** screen under **AI Actions**.
  - Loads enabled providers from SQLite.
  - Uses the provider's saved encrypted credential.
  - Supports provider and model selection.
  - Accepts a task, current value/context, and desired outcome.
  - Produces an exact proposal, reason summary, confidence, provider, model, and elapsed time.
  - Preview-only: it never writes to WordPress.
- Added startup/DI registration and navigation visibility for AI Studio.

## Safety

AI Studio is preview-only. A result must still enter Suggested Changes and pass approval, backup, execution, and verification before WordPress is changed.

## Validation

- `MainWindow.xaml` was parsed successfully as XML.
- A .NET SDK was not available in the packaging environment, so run `dotnet build` and `dotnet test` locally.
