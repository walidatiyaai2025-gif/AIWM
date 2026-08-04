# Part 79 — Automatic Sidebar Foreground

- Sidebar text is now calculated from the actual sidebar background.
- Dark sidebar backgrounds use light text; light sidebar backgrounds use dark text.
- The saved palette and theme mode are applied immediately when ThemeService is created.
- Navigation buttons keep sidebar-specific colors even when a command is temporarily disabled.
- Group headers, labels, search text, muted captions, hover, borders, and disabled items use dedicated contrast-safe tokens.
- Added explicit TextElement.Foreground propagation to prevent nested presenters from falling back to page text colors.
