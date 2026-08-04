# AI WordPress Manager — User Journey Roadmap

Current application version: **v1.3.9**  
Platform: **Windows Desktop / WPF / .NET 8**  
Journey principle: **Offline-first, safe-by-default, reversible execution**

## Canonical user journey

1. **Open application**
   - Load local SQLite data first.
   - Restore the last selected site and last known workspace state.
   - Show startup progress and current application version.

2. **Select or add a WordPress site**
   - Validate URL and credentials.
   - Test WordPress REST connectivity.
   - Save secrets securely.
   - Create an initial local site snapshot.

3. **Synchronize**
   - Display cached content immediately.
   - Synchronize posts, pages, media, categories, tags, themes and site settings.
   - Keep the previous snapshot available until synchronization succeeds.
   - Record last successful synchronization and errors.

4. **Analyze**
   - Run content, SEO, links, design, responsive, performance and accessibility checks.
   - Group findings by severity, feature and affected item.
   - Preserve the source evidence used by every finding.

5. **Generate recommendations**
   - Combine deterministic application rules with AI recommendations.
   - Show expected impact, risk, confidence and affected WordPress objects.
   - Never execute destructive changes directly from analysis.

6. **Preview changes**
   - Display before/after values.
   - Show screenshots for visual changes when available.
   - Highlight deletions, redirects, metadata changes and media impact.

7. **Approve**
   - Support single and multi-select approval.
   - Allow approval, rejection, defer and edit-before-approval.
   - Keep an audit record for every decision.

8. **Create safety backup**
   - Verify SQLite integrity.
   - Save a local database backup.
   - Capture WordPress pre-change values required for rollback.
   - Block execution when the safety package is incomplete.

9. **Execute**
   - Queue approved changes in Execution Center.
   - Execute with progress, retries, pause and cancellation.
   - Record WordPress request/response details without exposing secrets.

10. **Verify**
    - Re-read the changed WordPress object.
    - Confirm expected values and page availability.
    - Re-run the relevant audit checks.
    - Mark the operation verified, partially verified or failed.

11. **Rollback**
    - Offer rollback for failed or unwanted changes.
    - Restore saved WordPress values and local state.
    - Verify the rollback and retain the full audit trail.

12. **Measure results**
    - Update SEO and health history.
    - Show completed improvements, unresolved risks and next recommended action.
    - Continue the journey from the highest-value safe action.

## Global UX rules

- Every screen must clearly show the selected site.
- Every grid must support filtering, pagination and multi-select where actions apply.
- Data screens load SQLite snapshots before live synchronization.
- Every long operation exposes progress, current step and a copyable error.
- Arabic RTL and English LTR must cover navigation, dialogs, statuses and generated guidance.
- Light and dark themes must preserve readable contrast.
- Destructive actions require explicit approval and a rollback plan.
- The application must always suggest the next logical journey step.

## Main navigation alignment

| Journey stage | Primary screen |
|---|---|
| Site setup | Sites |
| Local content review | WordPress Explorer |
| Analysis | Content Audit, SEO Audit, Broken Links, Design and Performance screens |
| Recommendations | Suggested Changes, AI Studio |
| Approval | Approval Queue |
| Safety | Backups, Transaction Center |
| Execution | Execution Center, Jobs |
| Verification | Evidence Center, Health Center, Reports |
| Rollback | Transaction Center, Backups |
| Progress measurement | Dashboard, SEO History, Activity Timeline |

## Current implementation status

### Implemented foundations

- WPF desktop shell with modern sidebar.
- SQLite and Entity Framework Core offline storage.
- Arabic/English localization infrastructure.
- Light/dark theme and configurable palettes.
- Site management and WordPress Explorer.
- Content Audit, SEO-related screens and suggested changes.
- Approval, execution, jobs, backups and restore foundations.
- Activity, logs, reports and health-related centers.
- Splash progress, footer version and automatic assembly version display.
- GitHub Windows build workflow.

### Priority implementation sequence

1. **Journey State Card on Dashboard**
   - Show current stage, completed stages, blockers and one primary next-action button.

2. **Unified selected-site context**
   - Ensure every screen immediately reflects the active site and cached data.

3. **Recommendation-to-execution continuity**
   - Preserve selected items and context while moving from Suggested Changes to Approval Queue and Execution Center.

4. **Execution receipts and verification**
   - Produce a readable receipt with before/after, API response, verification and rollback availability.

5. **Failure recovery experience**
   - Copyable error, AI/app solution, retry, skip, pause and rollback from one dialog.

6. **Full localization and visual consistency pass**
   - Translate remaining runtime strings and standardize headers, buttons, empty states and status colors.

7. **Release readiness**
   - Build, automated tests, publish artifact, setup package and release notes.

## Definition of done for each feature

A feature is complete only when:

- It loads local data first.
- It supports the active site context.
- It works in Arabic and English.
- It works in light and dark themes.
- It exposes loading, empty, success and failure states.
- It logs meaningful operations.
- It fits into the canonical user journey.
- Risky changes have preview, approval, backup, verification and rollback.
- The complete solution builds successfully.
