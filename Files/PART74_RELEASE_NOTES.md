# Part 74 — Suggested Changes Selection, Direct Execute, Grid Menu, SEO History Compatibility

## Fixed
- Clicking any Suggested Changes row now forces full-row selection and updates the details panel immediately.
- The selected item is synchronized explicitly with `SuggestedChangesViewModel.SelectedItem`.
- Existing databases now create the `SeoAuditSnapshots` table and index defensively at startup when the migration was missed.

## Added
- `Execute selected now` action in the Suggested Changes page toolbar.
- Right-click grid menu with:
  - Execute selected now
  - Explain suggestion
  - Approve
  - Reject
  - Copy current value
  - Copy proposed value
- Parameterized row-level approve/reject commands.

## Safety
- Direct execution still respects `CanApplyDirectly` and shows manual-review guidance for unsupported actions.
- Existing backup, approval, execution, and verification stages are preserved.
