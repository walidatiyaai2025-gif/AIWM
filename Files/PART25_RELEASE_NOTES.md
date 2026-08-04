# Part 25 — Zero-Credit AI and Actionable Suggestions

- Added Ollama as the first-priority local AI provider.
- Ollama uses `http://localhost:11434` and requires no API key or paid API credit.
- Default local model: `qwen3:4b`.
- Added automatic model discovery through Ollama `/api/tags`.
- Changed provider fallback so OpenAI 429/credit failures no longer terminate suggestion generation.
- Added a built-in actionable recommendation fallback when every AI provider is unavailable.
- Replaced several placeholder recommendations with concrete proposed actions.
- Broken-link suggestions now propose removing the broken hyperlink while preserving anchor text until a verified URL exists.
- Settings now describe Ollama as the recommended zero-credit provider.

## Ollama setup

1. Install Ollama for Windows.
2. Open PowerShell.
3. Run: `ollama pull qwen3:4b`
4. Keep Ollama running.
5. In Settings, enable Ollama, set priority 1, model `qwen3:4b`, and click Test connection.
6. No API key is required.
