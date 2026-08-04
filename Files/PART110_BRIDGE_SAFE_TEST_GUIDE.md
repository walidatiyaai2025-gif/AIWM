# Part 110 — Bridge safe test guide

## Install or update the plugin

1. Open **Design & Quality → Visual Editor → Bridge diagnostics**.
2. Click **Open bundled plugin**.
3. In WordPress, open **Plugins → Add New → Upload Plugin** or use **Open WordPress upload**.
4. Upload `AIWordPressManager-Bridge-1.2.0.zip` and activate/update it.

## Verify without changing WordPress

1. Load a public page in Visual Editor.
2. Select an element.
3. Enter a harmless CSS declaration, for example `outline: 2px solid red;`.
4. Click **Run full diagnostics**.
5. Click **Run safe dry-run**.

The dry-run validates authentication, permissions, selector safety, CSS safety, active stylesheet, current managed CSS checksum, and existing managed-rule count. It does not write Custom CSS.

## Execution gate

The **Execute on WordPress** command is blocked unless:

- Full Bridge diagnostics passed within the last 15 minutes.
- The safe dry-run passed within the last 15 minutes.
- The selected element and CSS have not changed since the dry-run.
- A Before screenshot exists.
- The current user has `edit_theme_options`.

Changing the selected element or CSS invalidates the safe test and requires a new dry-run.
