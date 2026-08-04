# Part 23 - OpenAI Context Hardening

- Reduced AI batch size from 12 to 3.
- Reduced per-field input limits before serialization.
- Added recursive split retry.
- Added an ultra-compact single-item fallback for very small model context windows.
- Preserves AI-only recommendation generation while preventing the application from failing on oversized requests.
- Important: clean `bin` and `obj` before running so Visual Studio does not execute the previous Part 21 binary.
