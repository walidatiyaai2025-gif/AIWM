using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Applies a lightweight, page-scoped visual pass to Suggested Changes.
/// It runs when that page becomes active, never on a timer, and never scans or
/// modifies controls belonging to another workspace.
/// </summary>
internal static class SuggestedChangesProfessionalExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> StyledPages = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (Attached.TryGetValue(window, out _))
            return;

        var state = new State(window);
        Attached.Add(window, state);
        state.Attach();
    }

    private static void Apply(MainWindow window)
    {
        if (!IsSuggestedChangesActive(window.DataContext))
            return;

        var heading = FindTextBlock(window, "Suggested Changes");
        if (heading is null || !heading.IsVisible)
            return;

        var pageRoot = FindNearestPageRoot(heading, window);
        if (pageRoot is null)
            return;

        if (!StyledPages.TryGetValue(pageRoot, out _))
        {
            StyledPages.Add(pageRoot, new object());
            pageRoot.UseLayoutRounding = true;
            pageRoot.SnapsToDevicePixels = true;
            ApplyStaticLayout(pageRoot, window);
        }

        // Newly generated rows/columns still receive the inexpensive virtualization flags.
        foreach (var grid in Enumerate<DataGrid>(pageRoot))
            ApplyGridPerformance(grid);
    }

    private static void ApplyStaticLayout(FrameworkElement pageRoot, Window window)
    {
        foreach (var grid in Enumerate<DataGrid>(pageRoot))
        {
            ApplyGridPerformance(grid);
            grid.RowHeight = 34;
            grid.ColumnHeaderHeight = 38;
            grid.MinRowHeight = 32;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.CanUserResizeColumns = true;
            grid.CanUserReorderColumns = true;
            grid.CanUserSortColumns = true;
            grid.FrozenColumnCount = Math.Min(2, grid.Columns.Count);
            grid.Margin = new Thickness(0, 10, 0, 8);
        }

        foreach (var button in Enumerate<Button>(pageRoot))
        {
            if (IsFloatingRailButton(button, window))
            {
                button.Visibility = Visibility.Collapsed;
                button.IsHitTestVisible = false;
                continue;
            }

            var label = button.Content?.ToString()?.Trim() ?? string.Empty;
            if (label is "Refresh" or "Execute selected now" or "Apply safe selected" or "Generate from audits")
            {
                button.MinHeight = 38;
                button.MinWidth = label == "Refresh" ? 82 : 142;
                button.Padding = new Thickness(14, 7, 14, 7);
                button.Margin = new Thickness(5, 0, 0, 0);
                button.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        var heading = FindTextBlock(pageRoot, "Suggested Changes");
        if (heading is not null)
        {
            heading.FontSize = 29;
            heading.FontWeight = FontWeights.Bold;
            heading.Margin = new Thickness(0, 0, 0, 4);
        }

        RemoveEmptyFloatingRails(pageRoot, window);
    }

    private static void ApplyGridPerformance(DataGrid grid)
    {
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        grid.UseLayoutRounding = true;
        grid.SnapsToDevicePixels = true;

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(grid, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(grid, true);
    }

    private static bool IsSuggestedChangesActive(object? dataContext)
    {
        if (dataContext is null)
            return false;

        var property = dataContext.GetType().GetProperty(
            "CurrentPage",
            BindingFlags.Instance | BindingFlags.Public);

        var value = property?.GetValue(dataContext)?.ToString();
        return string.Equals(value, "Suggested Changes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFloatingRailButton(Button button, Window window)
    {
        if (button.ActualWidth <= 0 || button.ActualHeight <= 0 || button.ActualWidth > 54)
            return false;

        var label = button.Content?.ToString()?.Trim() ?? string.Empty;
        if (label.Length > 3)
            return false;

        try
        {
            var point = button.TransformToAncestor(window).Transform(new Point(0, 0));
            return point.X > window.ActualWidth - 85 && point.Y > 260;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void RemoveEmptyFloatingRails(DependencyObject root, Window window)
    {
        foreach (var panel in Enumerate<StackPanel>(root).ToArray())
        {
            if (panel.Orientation != Orientation.Vertical || panel.Children.Count < 3 || panel.ActualWidth > 80)
                continue;

            try
            {
                var point = panel.TransformToAncestor(window).Transform(new Point(0, 0));
                if (point.X <= window.ActualWidth - 100 || point.Y < 240)
                    continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            panel.Visibility = Visibility.Collapsed;
            panel.IsHitTestVisible = false;
        }
    }

    private static FrameworkElement? FindNearestPageRoot(FrameworkElement heading, Window window)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(heading);
        while (current is not null && !ReferenceEquals(current, window))
        {
            if (current is FrameworkElement element &&
                element.IsVisible &&
                element.ActualWidth > 700 &&
                element.ActualHeight > 300)
                return element;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string exactText)
    {
        foreach (var text in Enumerate<TextBlock>(root))
        {
            if (text.IsVisible &&
                string.Equals(text.Text?.Trim(), exactText, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D)
            yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child))
                yield return nested;
        }
    }

    private sealed class State(MainWindow window)
    {
        private INotifyPropertyChanged? _dataContext;
        private bool _pending;
        private bool _disposed;
        private bool _contentRenderedHandled;

        public void Attach()
        {
            window.DataContextChanged += OnDataContextChanged;
            window.ContentRendered += OnContentRendered;
            window.Closed += OnClosed;
            AttachDataContext(window.DataContext as INotifyPropertyChanged);
            Schedule();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachDataContext(e.NewValue as INotifyPropertyChanged);
            Schedule();
        }

        private void AttachDataContext(INotifyPropertyChanged? value)
        {
            if (_dataContext is not null)
                _dataContext.PropertyChanged -= OnPropertyChanged;

            _dataContext = value;

            if (_dataContext is not null)
                _dataContext.PropertyChanged += OnPropertyChanged;
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, "CurrentPage", StringComparison.Ordinal))
                Schedule();
        }

        public void Schedule()
        {
            if (_disposed || _pending || !IsSuggestedChangesActive(window.DataContext))
                return;

            _pending = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _pending = false;
                if (!_disposed && window.IsLoaded)
                    Apply(window);
            }));
        }

        private void OnContentRendered(object? sender, EventArgs e)
        {
            if (_contentRenderedHandled)
                return;

            _contentRenderedHandled = true;
            window.ContentRendered -= OnContentRendered;
            Schedule();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            AttachDataContext(null);
            window.DataContextChanged -= OnDataContextChanged;
            window.ContentRendered -= OnContentRendered;
            window.Closed -= OnClosed;
        }
    }
}
