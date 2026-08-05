using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Performs a one-time visual cleanup of the Suggested Changes workspace.
/// The implementation intentionally avoids timers and keeps all heavy work
/// virtualized inside the existing DataGrid.
/// </summary>
internal static class SuggestedChangesProfessionalExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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
        window.ContentRendered += state.OnContentRendered;
        window.Closed += state.OnClosed;
        state.Schedule();
    }

    private static void Apply(MainWindow window)
    {
        var heading = FindTextBlock(window, "Suggested Changes");
        if (heading is null)
            return;

        var pageRoot = FindPageRoot(heading, window);
        if (pageRoot is null)
            return;

        pageRoot.UseLayoutRounding = true;
        pageRoot.SnapsToDevicePixels = true;

        foreach (var grid in Enumerate<DataGrid>(pageRoot))
        {
            grid.EnableRowVirtualization = true;
            grid.EnableColumnVirtualization = true;
            grid.RowHeight = 34;
            grid.ColumnHeaderHeight = 38;
            grid.MinRowHeight = 32;
            grid.HeadersVisibility = DataGridHeadersVisibility.Column;
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
            grid.CanUserResizeColumns = true;
            grid.CanUserReorderColumns = true;
            grid.CanUserSortColumns = true;
            grid.FrozenColumnCount = Math.Min(2, grid.Columns.Count);
            grid.Margin = new Thickness(0, 10, 0, 8);

            VirtualizingPanel.SetIsVirtualizing(grid, true);
            VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
            ScrollViewer.SetCanContentScroll(grid, true);
            ScrollViewer.SetIsDeferredScrollingEnabled(grid, true);
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

        foreach (var text in Enumerate<TextBlock>(pageRoot))
        {
            if (string.Equals(text.Text?.Trim(), "Suggested Changes", StringComparison.OrdinalIgnoreCase))
            {
                text.FontSize = 29;
                text.FontWeight = FontWeights.Bold;
                text.Margin = new Thickness(0, 0, 0, 4);
            }
        }

        RemoveEmptyFloatingRails(pageRoot, window);
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
        catch
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
            catch
            {
                continue;
            }

            panel.Visibility = Visibility.Collapsed;
            panel.IsHitTestVisible = false;
        }
    }

    private static FrameworkElement? FindPageRoot(FrameworkElement heading, Window window)
    {
        FrameworkElement? best = null;
        DependencyObject? current = heading;

        while (current is not null && !ReferenceEquals(current, window))
        {
            if (current is FrameworkElement element && element.ActualWidth > 700 && element.ActualHeight > 350)
                best = element;

            current = VisualTreeHelper.GetParent(current);
        }

        return best;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string exactText)
    {
        foreach (var text in Enumerate<TextBlock>(root))
        {
            if (string.Equals(text.Text?.Trim(), exactText, StringComparison.OrdinalIgnoreCase))
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
        private bool _pending;
        private bool _disposed;

        public void Schedule()
        {
            if (_disposed || _pending)
                return;

            _pending = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _pending = false;
                if (!_disposed && window.IsLoaded)
                    Apply(window);
            }));
        }

        public void OnContentRendered(object? sender, EventArgs e) => Schedule();

        public void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            window.ContentRendered -= OnContentRendered;
            window.Closed -= OnClosed;
        }
    }
}
