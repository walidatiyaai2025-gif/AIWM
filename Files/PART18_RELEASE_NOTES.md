# Part 18 — Theme Inspector and Post SEO Editor

## Theme Inspector
- Reads the authenticated WordPress themes REST endpoint when available.
- Falls back to public homepage theme-folder detection when permissions are limited.
- Displays theme name, stylesheet, parent template, version, author, discovery method, and detected REST capabilities.
- Does not modify PHP, theme files, plugins, or WordPress settings.

## Post & Page SEO Editor
- Loads the post/page list from the offline SQLite snapshot.
- Loads live editable WordPress fields only after the user selects an item.
- Supports title, slug, status, content, excerpt, featured media, categories, tags, template, comments, pings, and sticky state.
- Creates a WordPress JSON snapshot and local SQLite backup before every update.
- Requires explicit confirmation before writing to WordPress.
- Resynchronizes the offline cache after a successful update.
- Provides a measurable on-page SEO score and recommendations.

## SEO plugin limitation
Yoast SEO, Rank Math, AIOSEO, and SEOPress metadata is not written unless the plugin exposes supported REST metadata or the secure companion connector is installed. The application does not invent private meta keys.
