# Part 24 — Multi-provider AI and global error popup

- Added Gemini, Groq, OpenRouter, and OpenAI providers behind `IAiProvider`.
- Added provider priorities and automatic fallback.
- Migrated legacy OpenAI settings automatically through key/value application settings; no manual database work is required.
- Added encrypted API-key storage per provider using the existing Windows DPAPI service.
- Added provider connection testing; Gemini also returns available `generateContent` models.
- Added compact 3-item batching to reduce context-window and free-tier usage.
- Added a global WPF error popup with friendly message, provider/status details, correlation ID, full exception, redaction, copy button, and logs-folder button.
- Improved gold text on light surfaces to `#8B6500`.

## Recommended free-first priority
1. Gemini
2. Groq
3. OpenRouter (`openrouter/free`)
4. OpenAI

API quotas and model availability are controlled by each provider account and may change.
