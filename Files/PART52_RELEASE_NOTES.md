# Part 52 — Stable Runtime Localization Fix

## Fixed
- Prevented `VisualTreeHelper.GetChildrenCount` from receiving `Run`, `Span`, or other non-visual `TextElement` objects.
- Removed recursive logical-tree traversal that could enumerate DataGrid rows and stall startup.
- Deferred localization until WPF reaches `DispatcherPriority.ContextIdle` so the main window renders first.
- Added reference-based visited tracking to avoid duplicate traversal.
- Preserved Arabic/English switching, RTL/LTR, DataGrid headers, tooltips, and inline text translation.

## Runtime behavior
- Visual controls are traversed through `VisualTreeHelper` only.
- `Run` and `Span` are translated only through the visible owning `TextBlock`.
- Multiple simultaneous language refreshes are coalesced into one queued operation.
