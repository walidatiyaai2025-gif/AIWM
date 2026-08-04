# Managed Visual CSS History Test Guide

1. Install and activate `WordPressPlugins/AIWordPressManager-Bridge-1.3.0.zip`.
2. In the desktop app, open **Design & Quality → Visual Editor → Bridge diagnostics**.
3. Run **Full diagnostics**. Both Managed CSS history route checks must pass.
4. Select a page element, enter safe CSS, capture Before evidence, run the safe dry-run, then execute the change.
5. Open the **Managed changes** tab and click **Refresh history**.
6. Confirm the new item shows ACTIVE, its selector, CSS, page URL, theme, user, and execution time.
7. Select the item and click **Rollback selected**.
8. Confirm WordPress accepts the rollback, the public page reloads, and the item changes to ROLLED BACK.
9. Open **System → API Logs** and confirm entries exist for:
   - Load managed Visual CSS history
   - Rollback managed Visual CSS history item
10. Verify the response body, HTTP status, duration, and correlation data are present.
