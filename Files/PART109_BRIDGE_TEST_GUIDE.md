# Part 109 — WordPress Bridge diagnostics and verified packaging

## What the full test checks

Open **Design & Quality → Visual Editor → Bridge diagnostics** and click **Run full diagnostics**.

The test is read-only and checks:

1. Saved WordPress credentials can authenticate.
2. `/wp-json/aiwp-manager/v1/health` responds.
3. `/wp-json/aiwp-manager/v1/visual-css` responds.
4. Visual CSS and rollback routes are discoverable.
5. Bridge version is 1.1.0 or newer.
6. The user has `edit_posts` and `edit_theme_options`.
7. WordPress, PHP, active theme, stylesheet, SEO plugins, and page builders are reported.
8. Every response is written to `wordpress-api.log`.

A green **READY** result means the bridge is ready for Visual CSS execution. The test itself does not write any CSS.

## Safe execution test

1. Select a non-critical page.
2. Load it in Visual Editor.
3. Select a harmless element.
4. Use a reversible declaration such as `outline: 2px solid red;`.
5. Capture Before.
6. Execute on WordPress.
7. Confirm the reloaded computed style and After evidence.
8. Click Rollback and verify the managed CSS returns to its previous revision.

## Offline package validation

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build\Validate-WordPressBridge.ps1
```

This verifies PHP syntax when PHP is installed, required routes/version markers, and exact source-to-ZIP integrity.
