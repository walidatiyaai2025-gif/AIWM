# How to test WordPress execution and API response logs

1. Open **SITE & DATA → Sites**.
2. Select the site, open Edit, and run **Test connection**. This is read-only.
3. Open **SYSTEM → API Logs**, press Refresh, and verify the GET/authentication responses.
4. Run **Content Audit** or **SEO Audit**, then generate Suggested Changes.
5. In **Suggested Changes**, select a low-risk supported content change and approve it.
6. Open **Execution Center**, build the plan, select a Ready row, then press Execute selected.
7. Confirm the backup prompt. The application then reads WordPress, sends the update, reads the object again, and verifies the new value.
8. Return to **API Logs**, press Refresh, and inspect Method, Endpoint, HTTP status, duration, request body, response body, correlation ID, and AI interpretation.
9. Test rollback from Execution Center on an Executed row, then confirm the rollback REST requests in API Logs.

Supported direct writes: title, slug, excerpt, status, and content. High-risk, staging-required, unsupported visual/theme/media actions, or rows without a concrete value do not write to WordPress.
