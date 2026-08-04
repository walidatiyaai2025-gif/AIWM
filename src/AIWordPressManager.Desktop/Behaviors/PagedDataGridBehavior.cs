using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AIWordPressManager.Desktop.Behaviors;

public static class PagedDataGridBehavior
{
    // Weak-key storage allows closed/unloaded screens and grids to be collected.
    private static readonly ConditionalWeakTable<DataGrid, GridState> States = new();
    private static readonly ConditionalWeakTable<DataGrid, Marker> Attaching = new();
    private static readonly ConditionalWeakTable<DataGrid, Marker> Deferred = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SearchablePropertyCache = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(PagedDataGridBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            grid.Loaded += OnGridLoaded;
            grid.Unloaded += OnGridUnloaded;
            grid.IsVisibleChanged += OnGridIsVisibleChanged;
        }
        else
        {
            grid.Loaded -= OnGridLoaded;
            grid.Unloaded -= OnGridUnloaded;
            grid.IsVisibleChanged -= OnGridIsVisibleChanged;
            Deferred.Remove(grid);
            Attaching.Remove(grid);
            DisposeState(grid);
        }
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            QueueAttachWhenVisible(grid);
        }
    }

    private static void OnGridIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.IsVisible)
        {
            QueueAttachWhenVisible(grid);
        }
    }

    private static void QueueAttachWhenVisible(DataGrid grid)
    {
        if (!grid.IsLoaded || !grid.IsVisible || States.TryGetValue(grid, out _) ||
            Attaching.TryGetValue(grid, out _) || !TryAddMarker(Deferred, grid))
        {
            return;
        }

        _ = grid.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                Deferred.Remove(grid);
                if (!grid.IsLoaded || !grid.IsVisible || States.TryGetValue(grid, out _))
                {
                    return;
                }

                TryAddMarker(Attaching, grid);
                try
                {
                    var state = new GridState(grid);
                    // Register before re-parenting. Re-parenting raises Loaded/Unloaded.
                    States.Add(grid, state);
                    if (!state.TryAttach())
                    {
                        States.Remove(grid);
                        state.Dispose();
                        return;
                    }

                    state.Refresh();
                }
                finally
                {
                    Attaching.Remove(grid);
                }
            }));
    }

    private static void OnGridUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || Attaching.TryGetValue(grid, out _))
        {
            return;
        }

        // Navigation uses Visibility for screens. Do not tear down a pager merely
        // because WPF temporarily unloads the grid while templates are changing.
        if (!grid.IsLoaded && grid.Parent is null)
        {
            DisposeState(grid);
        }
    }

    private static void DisposeState(DataGrid grid)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.Dispose();
            States.Remove(grid);
        }
    }

    public static void ReleaseHiddenGridCaches()
    {
        // CWT entries disappear automatically. This method clears page filters for
        // hidden grids that are still alive in the navigation shell.
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            ReleaseHiddenInVisualTree(window);
        }
    }

    private static void ReleaseHiddenInVisualTree(DependencyObject root)
    {
        if (root is DataGrid grid && !grid.IsVisible && States.TryGetValue(grid, out var state))
        {
            state.ReleasePageCache();
        }
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) return;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++) ReleaseHiddenInVisualTree(VisualTreeHelper.GetChild(root, i));
    }

    private static bool TryAddMarker(ConditionalWeakTable<DataGrid, Marker> table, DataGrid grid)
    {
        if (table.TryGetValue(grid, out _)) return false;
        try { table.Add(grid, new Marker()); return true; }
        catch (ArgumentException) { return false; }
    }

    private sealed class Marker { }

    private sealed class GridState : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly TextBox _searchBox;
        private readonly ComboBox _pageSizeBox;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _pageText;
        private readonly Button _firstButton;
        private readonly Button _previousButton;
        private readonly Button _nextButton;
        private readonly Button _lastButton;
        private readonly Button _clearButton;
        private readonly DependencyPropertyDescriptor? _itemsSourceDescriptor;
        private readonly HashSet<object> _visibleItems = new(ReferenceEqualityComparer.Instance);
        private readonly DispatcherTimer _searchDebounceTimer;

        private Grid? _wrapper;
        private object? _originalParent;
        private int _originalIndex = -1;
        private object? _originalContent;
        private INotifyCollectionChanged? _observableSource;
        private ICollectionView? _view;
        private int _pageNumber = 1;
        private int _pageSize = 25;
        private int _totalPages = 1;
        private int _filteredCount;
        private bool _disposed;
        private bool _isRefreshing;
        private bool _refreshQueued;
        private readonly Predicate<object> _pageFilter;

        public GridState(DataGrid grid)
        {
            _grid = grid;
            _searchBox = new TextBox
            {
                MinWidth = 220,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 8),
                ToolTip = "Filter rows across all visible fields",
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _searchBox.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            _searchBox.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            _searchBox.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");

            _pageSizeBox = new ComboBox
            {
                Width = 82,
                Height = 34,
                Margin = new Thickness(0, 0, 10, 8),
                ItemsSource = new[] { 10, 25, 50, 100 },
                SelectedItem = 25,
                ToolTip = "Rows per page"
            };

            _summaryText = CreateTextBlock();
            _summaryText.VerticalAlignment = VerticalAlignment.Center;
            _summaryText.Margin = new Thickness(0, 0, 0, 8);

            _pageText = CreateTextBlock();
            _pageText.VerticalAlignment = VerticalAlignment.Center;
            _pageText.Margin = new Thickness(10, 0, 10, 0);

            _firstButton = CreatePagerButton("⏮", "First page");
            _previousButton = CreatePagerButton("◀", "Previous page");
            _nextButton = CreatePagerButton("▶", "Next page");
            _lastButton = CreatePagerButton("⏭", "Last page");
            _clearButton = CreatePagerButton("Clear filter", "Clear current filter");
            _clearButton.MinWidth = 92;

            _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(280)
            };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                _pageNumber = 1;
                Refresh();
            };

            _pageFilter = item => item is not null && _visibleItems.Contains(item);

            _itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty,
                typeof(DataGrid));
        }

        public bool TryAttach()
        {
            if (!TryReplaceInParent(_grid, out _wrapper))
            {
                return false;
            }

            var toolbar = BuildToolbar();
            var footer = BuildFooter();
            AttachGridContextMenu();

            _wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _wrapper.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _wrapper.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(toolbar, 0);
            Grid.SetRow(_grid, 1);
            Grid.SetRow(footer, 2);
            _wrapper.Children.Add(toolbar);
            _wrapper.Children.Add(_grid);
            _wrapper.Children.Add(footer);

            _searchBox.TextChanged += OnSearchChanged;
            _pageSizeBox.SelectionChanged += OnPageSizeChanged;
            _firstButton.Click += (_, _) => ChangePage(1);
            _previousButton.Click += (_, _) => ChangePage(_pageNumber - 1);
            _nextButton.Click += (_, _) => ChangePage(_pageNumber + 1);
            _lastButton.Click += (_, _) => ChangePage(_totalPages);
            _clearButton.Click += (_, _) => _searchBox.Clear();
            _itemsSourceDescriptor?.AddValueChanged(_grid, OnItemsSourceChanged);

            SubscribeToSource();
            return true;
        }

        public void Refresh()
        {
            if (_disposed || _isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                SubscribeToSource();
                var nextView = CollectionViewSource.GetDefaultView(_grid.ItemsSource);
                if (nextView is null)
                {
                    UpdateFooter(0, 0);
                    return;
                }

                if (_view is not null && !ReferenceEquals(_view, nextView) && ReferenceEquals(_view.Filter, _pageFilter))
                {
                    _view.Filter = null;
                }
                _view = nextView;

                var query = _searchBox.Text.Trim();
                var totalCount = 0;
                var filteredCount = 0;

                // First pass counts only. It avoids retaining a second full copy of large grids.
                foreach (var item in GetSourceItems(_view))
                {
                    totalCount++;
                    if (string.IsNullOrWhiteSpace(query) || Matches(item, query))
                    {
                        filteredCount++;
                    }
                }

                _filteredCount = filteredCount;
                _totalPages = Math.Max(1, (int)Math.Ceiling(_filteredCount / (double)_pageSize));
                _pageNumber = Math.Clamp(_pageNumber, 1, _totalPages);

                var skip = (_pageNumber - 1) * _pageSize;
                var accepted = 0;
                var pageItems = new List<object>(Math.Min(_pageSize, _filteredCount));

                // Second pass retains only the requested page.
                foreach (var item in GetSourceItems(_view))
                {
                    if (!string.IsNullOrWhiteSpace(query) && !Matches(item, query))
                    {
                        continue;
                    }

                    if (accepted++ < skip)
                    {
                        continue;
                    }

                    pageItems.Add(item);
                    if (pageItems.Count >= _pageSize)
                    {
                        break;
                    }
                }

                _visibleItems.Clear();
                foreach (var item in pageItems)
                {
                    _visibleItems.Add(item);
                }

                // The guard prevents CollectionView refresh notifications from re-entering Refresh().
                if (!ReferenceEquals(_view.Filter, _pageFilter))
                {
                    _view.Filter = _pageFilter;
                }
                else
                {
                    _view.Refresh();
                }

                UpdateFooter(totalCount, pageItems.Count);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private DockPanel BuildToolbar()
        {
            var panel = new DockPanel { LastChildFill = true };

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            right.Children.Add(new TextBlock
            {
                Text = "Rows",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            });
            right.Children.Add(_pageSizeBox);
            right.Children.Add(_clearButton);
            DockPanel.SetDock(right, Dock.Right);

            var left = new Grid();
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = "Filter",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(_searchBox, 1);
            left.Children.Add(label);
            left.Children.Add(_searchBox);

            panel.Children.Add(right);
            panel.Children.Add(left);
            return panel;
        }

        private DockPanel BuildFooter()
        {
            var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };

            var pager = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            pager.Children.Add(_firstButton);
            pager.Children.Add(_previousButton);
            pager.Children.Add(_pageText);
            pager.Children.Add(_nextButton);
            pager.Children.Add(_lastButton);
            DockPanel.SetDock(pager, Dock.Right);

            footer.Children.Add(pager);
            footer.Children.Add(_summaryText);
            return footer;
        }

        private void AttachGridContextMenu()
        {
            var menu = _grid.ContextMenu ?? new ContextMenu();
            if (_grid.ContextMenu is null) _grid.ContextMenu = menu;

            // Preserve screen-specific menu items declared in XAML, then append the universal grid tools once.
            if (!menu.Items.OfType<MenuItem>().Any(x => Equals(x.Tag, "UniversalGridAction")))
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator { Tag = "UniversalGridAction" });
                menu.Items.Add(CreateMenuItem("Copy selected rows", (_, _) => CopySelectedRows(), "UniversalGridAction"));
                menu.Items.Add(CreateMenuItem("Copy current cell", (_, _) => CopyCurrentCell(), "UniversalGridAction"));
                menu.Items.Add(CreateMenuItem("Copy row as JSON", (_, _) => CopySelectedRowAsJson(), "UniversalGridAction"));
                menu.Items.Add(new Separator { Tag = "UniversalGridAction" });
                menu.Items.Add(CreateMenuItem("Export visible page to CSV", (_, _) => ExportVisiblePage(), "UniversalGridAction"));
                menu.Items.Add(CreateMenuItem("Export selected rows to CSV", (_, _) => ExportSelectedRows(), "UniversalGridAction"));
                menu.Items.Add(CreateMenuItem("Auto size columns", (_, _) =>
                {
                    foreach (var column in _grid.Columns) column.Width = DataGridLength.Auto;
                }, "UniversalGridAction"));
                menu.Items.Add(CreateMenuItem("Reset column widths", (_, _) =>
                {
                    foreach (var column in _grid.Columns) column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                }, "UniversalGridAction"));
            }

            menu.Opened -= OnContextMenuOpened;
            menu.Opened += OnContextMenuOpened;
            _grid.PreviewMouseRightButtonDown -= OnGridPreviewMouseRightButtonDown;
            _grid.PreviewMouseRightButtonDown += OnGridPreviewMouseRightButtonDown;
        }

        private void OnGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row is null) return;
            if (!row.IsSelected)
            {
                _grid.SelectedItems.Clear();
                row.IsSelected = true;
            }
            _grid.SelectedItem = row.Item;
            _grid.CurrentItem = row.Item;
        }

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            foreach (var dynamicItem in menu.Items.OfType<FrameworkElement>().Where(x => Equals(x.Tag, "DynamicGridCommand")).ToArray())
                menu.Items.Remove(dynamicItem);

            var actions = DiscoverAvailableActions();
            if (actions.Count == 0) return;

            menu.Items.Insert(0, new Separator { Tag = "DynamicGridCommand" });
            for (var index = actions.Count - 1; index >= 0; index--)
            {
                var action = actions[index];
                var item = new MenuItem
                {
                    Header = action.Label,
                    Command = action.Command,
                    CommandParameter = action.Parameter,
                    Tag = "DynamicGridCommand",
                    IsEnabled = action.Command.CanExecute(action.Parameter)
                };
                menu.Items.Insert(0, item);
            }
        }

        private List<GridCommandAction> DiscoverAvailableActions()
        {
            var result = new List<GridCommandAction>();
            var selected = _grid.SelectedItem;
            if (selected is null || _grid.DataContext is null) return result;

            foreach (var owner in EnumerateCommandOwners(_grid.DataContext, selected))
            {
                foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(x => typeof(ICommand).IsAssignableFrom(x.PropertyType) && x.Name.EndsWith("Command", StringComparison.Ordinal)))
                {
                    if (property.GetValue(owner) is not ICommand command) continue;
                    if (IsInfrastructureCommand(property.Name)) continue;

                    object? parameter = selected;
                    var executable = SafeCanExecute(command, parameter);
                    if (!executable && property.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase))
                    {
                        parameter = null;
                        executable = SafeCanExecute(command, null);
                    }
                    if (!executable) continue;

                    var label = HumanizeCommandName(property.Name);
                    if (result.Any(x => x.Label == label)) continue;
                    result.Add(new GridCommandAction(label, command, parameter));
                }
            }

            return result.Take(12).ToList();
        }

        private static IEnumerable<object> EnumerateCommandOwners(object root, object selected)
        {
            yield return root;
            foreach (var property in root.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string)) continue;
                object? value;
                try { value = property.GetValue(root); } catch { continue; }
                if (value is null) continue;

                var itemsProperty = value.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
                if (itemsProperty?.GetValue(value) is IEnumerable items && items.Cast<object>().Any(x => ReferenceEquals(x, selected) || Equals(x, selected)))
                    yield return value;
            }
        }

        private static bool SafeCanExecute(ICommand command, object? parameter)
        {
            try { return command.CanExecute(parameter); }
            catch { return false; }
        }

        private static bool IsInfrastructureCommand(string name)
            => name.StartsWith("Load", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Refresh", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Navigate", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("ClearSearch", StringComparison.OrdinalIgnoreCase)
               || name.Contains("AutoRefresh", StringComparison.OrdinalIgnoreCase);

        private static string HumanizeCommandName(string name)
        {
            var text = name.EndsWith("Command", StringComparison.Ordinal) ? name[..^7] : name;
            return Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2");
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child is not null)
            {
                if (child is T match) return match;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private sealed record GridCommandAction(string Label, ICommand Command, object? Parameter);

        private static MenuItem CreateMenuItem(string header, RoutedEventHandler click, string? tag = null)
        {
            var item = new MenuItem { Header = header, Tag = tag };
            item.Click += click;
            return item;
        }

        private void CopySelectedRowAsJson()
        {
            var row = _grid.SelectedItem;
            if (row is null) return;
            var json = System.Text.Json.JsonSerializer.Serialize(row, row.GetType(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Clipboard.SetText(json);
        }

        private void ExportSelectedRows()
        {
            var rows = _grid.SelectedItems.Cast<object>().ToArray();
            if (rows.Length == 0) return;
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"grid-selected-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true) return;

            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", _grid.Columns.Select(c => Csv(c.Header?.ToString() ?? string.Empty))));
            foreach (var row in rows)
                builder.AppendLine(string.Join(",", _grid.Columns.Select(c => Csv(GetColumnValue(c, row)))));
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
        }

        private void CopyCurrentCell()
        {
            var cell = _grid.CurrentCell;
            if (cell.Item is null || cell.Column is null) return;
            var value = GetColumnValue(cell.Column, cell.Item);
            if (value is not null) Clipboard.SetText(value);
        }

        private void CopySelectedRows()
        {
            var rows = _grid.SelectedItems.Cast<object>().ToArray();
            if (rows.Length == 0) return;
            var builder = new StringBuilder();
            builder.AppendLine(string.Join("\t", _grid.Columns.Select(c => c.Header?.ToString() ?? string.Empty)));
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join("\t", _grid.Columns.Select(c => GetColumnValue(c, row).Replace("\t", " ").Replace("\r", " ").Replace("\n", " "))));
            }
            Clipboard.SetText(builder.ToString());
        }

        private void ExportVisiblePage()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"grid-page-{_pageNumber}-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true) return;

            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", _grid.Columns.Select(c => Csv(c.Header?.ToString() ?? string.Empty))));
            foreach (var row in _visibleItems)
            {
                builder.AppendLine(string.Join(",", _grid.Columns.Select(c => Csv(GetColumnValue(c, row)))));
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        private static string GetColumnValue(DataGridColumn column, object item)
        {
            if (column is DataGridBoundColumn bound && bound.Binding is Binding binding && binding.Path?.Path is string path)
            {
                object? current = item;
                foreach (var segment in path.Split('.'))
                {
                    if (current is null) break;
                    current = current.GetType().GetProperty(segment)?.GetValue(current);
                }
                return current?.ToString() ?? string.Empty;
            }
            return item.ToString() ?? string.Empty;
        }

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            // Avoid re-filtering thousands of rows for every key stroke.
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OnPageSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_pageSizeBox.SelectedItem is int size)
            {
                _pageSize = size;
                _pageNumber = 1;
                Refresh();
            }
        }

        private void OnItemsSourceChanged(object? sender, EventArgs e)
        {
            if (_isRefreshing) return;
            _pageNumber = 1;
            SubscribeToSource();
            QueueRefresh();
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isRefreshing) return;
            QueueRefresh();
        }

        private void QueueRefresh()
        {
            if (_disposed || _refreshQueued) return;
            _refreshQueued = true;
            _ = _grid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                Refresh();
            }));
        }

        private void ChangePage(int page)
        {
            _pageNumber = Math.Clamp(page, 1, _totalPages);
            Refresh();
        }

        private void SubscribeToSource()
        {
            if (_observableSource is not null)
            {
                _observableSource.CollectionChanged -= OnCollectionChanged;
                _observableSource = null;
            }

            if (_grid.ItemsSource is INotifyCollectionChanged observable)
            {
                _observableSource = observable;
                _observableSource.CollectionChanged += OnCollectionChanged;
            }
            else if (CollectionViewSource.GetDefaultView(_grid.ItemsSource)?.SourceCollection is INotifyCollectionChanged sourceObservable)
            {
                _observableSource = sourceObservable;
                _observableSource.CollectionChanged += OnCollectionChanged;
            }
        }

        private void UpdateFooter(int totalCount, int visibleCount)
        {
            var start = _filteredCount == 0 ? 0 : ((_pageNumber - 1) * _pageSize) + 1;
            var end = _filteredCount == 0 ? 0 : start + visibleCount - 1;
            _summaryText.Text = $"Showing {start:N0}–{end:N0} of {_filteredCount:N0} filtered row(s) • {totalCount:N0} total";
            _pageText.Text = $"Page {_pageNumber:N0} of {_totalPages:N0}";

            _firstButton.IsEnabled = _pageNumber > 1;
            _previousButton.IsEnabled = _pageNumber > 1;
            _nextButton.IsEnabled = _pageNumber < _totalPages;
            _lastButton.IsEnabled = _pageNumber < _totalPages;
            _clearButton.IsEnabled = !string.IsNullOrWhiteSpace(_searchBox.Text);
        }

        private static IEnumerable<object> GetSourceItems(ICollectionView view)
        {
            if (view.SourceCollection is IEnumerable source)
            {
                foreach (var item in source)
                {
                    if (item is not null)
                    {
                        yield return item;
                    }
                }
            }
        }

        private static bool Matches(object item, string query)
        {
            var comparison = StringComparison.CurrentCultureIgnoreCase;
            if (item.ToString()?.Contains(query, comparison) == true)
            {
                return true;
            }

            foreach (var property in GetSearchableProperties(item.GetType()))
            {
                try
                {
                    var value = property.GetValue(item);
                    if (value?.ToString()?.Contains(query, comparison) == true)
                    {
                        return true;
                    }
                }
                catch
                {
                    // A failing calculated property must not break grid filtering.
                }
            }

            return false;
        }

        private static PropertyInfo[] GetSearchableProperties(Type type) =>
            SearchablePropertyCache.GetOrAdd(type, static currentType => currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .Where(property =>
                {
                    var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                    return propertyType.IsPrimitive ||
                           propertyType.IsEnum ||
                           propertyType == typeof(string) ||
                           propertyType == typeof(decimal) ||
                           propertyType == typeof(DateTime) ||
                           propertyType == typeof(DateTimeOffset) ||
                           propertyType == typeof(Guid);
                })
                .ToArray());

        private static TextBlock CreateTextBlock()
        {
            var text = new TextBlock { FontSize = 11 };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return text;
        }

        private static Button CreatePagerButton(string content, string toolTip)
        {
            var button = new Button
            {
                Content = content,
                ToolTip = toolTip,
                MinWidth = 38,
                Height = 32,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(2, 0, 2, 0),
                FontWeight = FontWeights.SemiBold
            };
            button.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            button.SetResourceReference(Control.ForegroundProperty, "PrimaryHoverBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            return button;
        }

        private bool TryReplaceInParent(FrameworkElement child, out Grid wrapper)
        {
            wrapper = new Grid
            {
                Margin = child.Margin,
                HorizontalAlignment = child.HorizontalAlignment,
                VerticalAlignment = child.VerticalAlignment,
                Width = child.Width,
                Height = child.Height,
                MinWidth = child.MinWidth,
                MinHeight = child.MinHeight,
                MaxWidth = child.MaxWidth,
                MaxHeight = child.MaxHeight
            };

            child.Margin = new Thickness(0);
            child.HorizontalAlignment = HorizontalAlignment.Stretch;
            child.VerticalAlignment = VerticalAlignment.Stretch;
            child.ClearValue(FrameworkElement.WidthProperty);
            child.ClearValue(FrameworkElement.HeightProperty);
            child.ClearValue(FrameworkElement.MinWidthProperty);
            child.ClearValue(FrameworkElement.MinHeightProperty);
            child.ClearValue(FrameworkElement.MaxWidthProperty);
            child.ClearValue(FrameworkElement.MaxHeightProperty);

            _originalParent = VisualTreeHelper.GetParent(child) ?? child.Parent;
            switch (_originalParent)
            {
                case Panel panel:
                    _originalIndex = panel.Children.IndexOf(child);
                    if (_originalIndex < 0) return false;
                    panel.Children.RemoveAt(_originalIndex);
                    panel.Children.Insert(_originalIndex, wrapper);
                    CopyGridPlacement(child, wrapper);
                    return true;

                case Decorator decorator when ReferenceEquals(decorator.Child, child):
                    decorator.Child = null;
                    decorator.Child = wrapper;
                    return true;

                case ContentControl contentControl when ReferenceEquals(contentControl.Content, child):
                    _originalContent = contentControl.Content;
                    contentControl.Content = wrapper;
                    return true;

                default:
                    return false;
            }
        }

        private static void CopyGridPlacement(UIElement source, UIElement target)
        {
            Grid.SetRow(target, Grid.GetRow(source));
            Grid.SetColumn(target, Grid.GetColumn(source));
            Grid.SetRowSpan(target, Grid.GetRowSpan(source));
            Grid.SetColumnSpan(target, Grid.GetColumnSpan(source));
            Panel.SetZIndex(target, Panel.GetZIndex(source));
        }

        public void ReleasePageCache()
        {
            _visibleItems.Clear();
            if (_view is not null && ReferenceEquals(_view.Filter, _pageFilter))
            {
                _view.Filter = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _searchDebounceTimer.Stop();
            _itemsSourceDescriptor?.RemoveValueChanged(_grid, OnItemsSourceChanged);
            if (_observableSource is not null)
            {
                _observableSource.CollectionChanged -= OnCollectionChanged;
            }

            if (_view is not null && ReferenceEquals(_view.Filter, _pageFilter))
            {
                _view.Filter = null;
            }
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
