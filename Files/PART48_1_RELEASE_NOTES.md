# Part 48.1 — SelectedItems Binding Fix

## Fixed

- Resolved the WPF dispatcher exception caused by binding an attached selected-items property TwoWay to read-only collection properties such as `PostSeoEditorViewModel.SelectedItems`.
- The shared `DataGridSelectedItemsBehavior.SelectedItems` attached property now defaults to OneWay binding.
- All current DataGrid selected-items bindings explicitly use `Mode=OneWay`.
- Programmatic and user selection synchronization is preserved because the behavior mutates the supplied collection and observes `INotifyCollectionChanged`.

## Affected screens

- Suggested Changes
- Execution Center
- Deletion Center — Posts and Pages
- Deletion Center — Media
- Post SEO Editor

No database migration is required.
