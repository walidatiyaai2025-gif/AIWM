# Phase 1 Part 21 — ChatGPT AI Recommendations and Compile Fix

- Fixes CS0165 in `ApprovedChangeExecutionService` by explicitly initializing the synchronized content row.
- Adds a dedicated readable `GoldOnLightBrush` (`#765600`) for any future gold text rendered on white or light surfaces.
- Adds OpenAI settings to the local SQLite-backed application settings without requiring a schema change.
- Protects the OpenAI API key with the existing Windows DPAPI service.
- Adds a ChatGPT/OpenAI recommendation provider using the Responses API.
- Requires ChatGPT AI configuration before Suggested Changes can be generated.
- Sends only recommendation context, never WordPress credentials or API keys, to the model.
- Keeps all AI proposals behind preview, approval, backup, verification, and rollback safeguards.

## Required user action

Open **Settings → ChatGPT AI recommendations**, enter an OpenAI API key, confirm the model name, save, then generate Suggested Changes.
