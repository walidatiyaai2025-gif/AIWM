# Visual CSS Executor — Test Guide

1. In WordPress, install and activate `AIWordPressManager-Bridge-1.1.0.zip`.
2. Ensure the saved WordPress user can customize the active theme (`edit_theme_options`).
3. In the desktop app, open `DESIGN & QUALITY → Visual Editor`.
4. Click **Check bridge**. The status must show `READY`.
5. Load a public page.
6. Click **Inspect element**, then select the exact element.
7. Click **Capture before**.
8. Enter CSS declarations only, for example:

   `font-size: 18px;`
   `line-height: 1.6;`

9. Click **Apply local preview** and inspect the result.
10. Click **Execute on WordPress**.
11. The app writes the managed CSS, reloads the page, verifies computed styles, and captures the After screenshot.
12. Open `SYSTEM → API Logs` to review the full WordPress response.
13. Click **Rollback** to restore the previous Custom CSS revision.
