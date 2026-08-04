# Part 26 — Puter AI Gateway + Actionable AI Brain Foundation

## Added
- Puter.js provider using an embedded WebView2 browser session.
- No developer API key is required for Puter.
- Interactive Puter sign-in from Settings → Test connection.
- Persistent WebView2 user profile under the application's local data directory.
- Puter model discovery through `puter.ai.listModels()`.
- Automatic fallback order now starts with Puter, then Ollama, Gemini, Groq, OpenRouter, and OpenAI.
- Puter session and provider failures participate in the existing global error reporting and provider fallback workflow.
- AI prompt rules now require exact replacement values or precise executable actions instead of generic recommendations.

## Important Puter billing model
Puter is not a conventional unlimited developer-funded API. It uses a user-pays model. The developer does not store an API key or pay for each user's requests; the signed-in Puter user account covers its own AI usage. Availability, limits, and charges are controlled by Puter and the selected model.

## Setup
1. Restore NuGet packages. The Desktop project now uses `Microsoft.Web.WebView2`.
2. Open Settings.
3. Enable Puter and set priority 1.
4. Select a Puter model such as `openai/gpt-5-nano`.
5. Click Test connection.
6. Sign in inside the Puter AI Connection window.
7. Save settings.
8. Generate Suggested Changes.

## Next implementation stage
The next stage is AI Website Brain — Stage 1:
- consolidate site, SEO, category, broken-link, and content findings into one site intelligence snapshot;
- rank opportunities by impact, confidence, risk, and effort;
- create exact suggested changes rather than narrative advice;
- track which provider produced each recommendation;
- prepare batch execution only after approval.
