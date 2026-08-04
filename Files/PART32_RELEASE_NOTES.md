# Part 32 — Browser Puter Authentication, Grouped Navigation, Live Icons, AI Pipeline

- Puter now uses the default Windows browser for account authentication and token creation.
- The Puter provider calls the official OpenAI-compatible Puter endpoint directly with the encrypted personal token; WebView sign-in is no longer required for normal AI requests.
- Added a dedicated Settings action that opens Puter in Chrome/default browser.
- Sidebar navigation is grouped into collapsible levels: Site & Data, Content & SEO, Design & Quality, AI Actions, and System.
- Existing active-page navigation highlighting is preserved on every child button.
- Dashboard status icons now pulse continuously to make live status visually obvious.
- Added the next AI execution-stage foundation: Auto-fix, Review, and Manual action counts calculated from saved Suggested Changes.
- All startup/offline SQLite loading from Part 30 remains unchanged.
