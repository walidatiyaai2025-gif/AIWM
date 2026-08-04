# Part 50 — Global Grid Pagination and Filtering

- Added one reusable pagination/filter behavior to every DataGrid through the global theme.
- Every grid now receives a filter box, page-size selector, row summary, and first/previous/next/last controls.
- Default page size is 25 rows with 10, 25, 50, and 100 options.
- Filtering searches all readable scalar fields of the row model.
- Pagination refreshes automatically when a collection changes or a grid receives a new ItemsSource.
- Existing sorting, selection, multi-select, commands, columns, and bindings remain on the original DataGrid.
- No database migration is required.
