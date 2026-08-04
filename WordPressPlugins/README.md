# WordPress plugin requirements

## Core REST workflow

The standard WordPress REST API and an Application Password are sufficient for basic post/page reads and writes: title, slug, excerpt, content, and status.

Required configuration:

1. WordPress 6.x recommended (minimum 5.6 for Application Passwords; the bundled bridge itself requires 6.0).
2. HTTPS enabled.
3. REST API reachable.
4. Pretty permalinks enabled.
5. A WordPress user with the permissions required by the selected action.
6. Username and Application Password saved in the desktop Sites screen.

## AI WordPress Manager Bridge 1.3.0

The bridge is required for Visual CSS execution and future specialist adapters. Install:

`AIWordPressManager-Bridge-1.3.0.zip`

from WordPress Admin → Plugins → Add New → Upload Plugin, then activate it.

Visual CSS requires the saved WordPress user to have:

- `edit_posts`
- `edit_theme_options`

The plugin exposes authenticated endpoints only:

- `GET /wp-json/aiwp-manager/v1/health`
- `GET|POST /wp-json/aiwp-manager/v1/visual-css`
- `POST /wp-json/aiwp-manager/v1/visual-css/rollback`
- `POST /wp-json/aiwp-manager/v1/visual-css/validate` (safe dry-run, no write)
- `GET /wp-json/aiwp-manager/v1/visual-css/history`
- `POST /wp-json/aiwp-manager/v1/visual-css/history/rollback`

The full bridge diagnostic test in Design & Quality → Visual Editor is read-only. It validates authentication, endpoints, route discovery, permissions, version compatibility, WordPress/PHP/theme context, and optional plugin detection. Actual CSS writes occur only after the user explicitly selects Execute on WordPress.

The bridge never returns credentials and does not allow anonymous writes.

## Optional detected plugins

Yoast SEO, Rank Math, Elementor, and Divi are optional. Detection does not imply write support; plugin-specific writes remain blocked until their adapters are implemented and verified.
