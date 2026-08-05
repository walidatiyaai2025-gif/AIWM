using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Applies inexpensive, one-time performance defaults to heavy WPF surfaces.
/// It also retires legacy/background work panels that should never cover the
/// user's active page. No polling timer or repeated whole-window scan is used.
/// </summary>
internal static class ApplicationPerformanceExperience
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> Optimized = new();
    private static readonly ConditionalWeakTable<MainWindow, WindowState> Windows = new();

    private static readonly string[] BackgroundTextMarkers =
    [
        "Real content analysis",
        "Local rules run against the synchronized WordPress snapshot",
        "Scanned 51 content items",
        "AI Copilot Inbox",
        "Live operations",
        "Priority resolution workspace",
        "Review workbenches",
        "Quick Fix Queue",
        "Journey completion"
    ];

    private static readonly string[] BackgroundTagMarkers =
    [
        "RealContentAnalysis",
        "FloatingWorkspace",
        "BackgroundWork",
        "AnalysisPopup",
        "AnalysisOverlay",
        "LiveOperationsPanel",
        "PriorityResolutionPanel",
        "ReviewWorkbenchesPanel",
        "AiCopilotInboxPanel"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnScrollViewerLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnElementLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (Windows.TryGetValue(window, out _))
            return;

        var state = new WindowState(window);
        Windows.Add(window, state);
        window.ContentRendered += state.OnContentRendered;
        window.Closed += state.OnClosed;

        state.ScheduleCleanup();
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DataGrid grid || !MarkOptimized(grid))
            return;

        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.IsReadOnly = true;
        grid.SnapsToDevicePixels = true;
        grid.UseLayoutRounding = true;

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(grid, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(grid, true);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ItemsControl items || items is DataGrid || !MarkOptimized(items))
            return;

        VirtualizingPanel.SetIsVirtualizing(items, true);
        VirtualizingPanel.SetVirtualizationMode(items, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(items, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(items, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(items, true);
    }

    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer viewer || !MarkOptimized(viewer))
            return;

        viewer.CanContentScroll = true;
        viewer.IsDeferredScrollingEnabled = true;
        viewer.PanningMode = PanningMode.Both;
        viewer.PanningDeceleration = 0.001;
        viewer.UseLayoutRounding = true;
        viewer.SnapsToDevicePixels = true;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element || element is MainWindow)
            return;

        if (Window.GetWindow(element) is not MainWindow)
            return;

        if (!LooksLikeBackgroundSurface(element))
            return;

        element.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => Retire(element)));
    }

    private static bool LooksLikeBackgroundSurface(FrameworkElement element)
    {
        if (element is not Border and not ContentControl and not Popup and not Panel)
            return false;

        var tag = element.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(tag) &&
            BackgroundTagMarkers.Any(marker => tag.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Text scanning is restricted to likely overlay containers only.
        if (element is not Border and not ContentControl)
            return false;

        if (element.ActualWidth > 0 && element.ActualWidth < 260)
            return false;

        var text = ReadText(element, maximumNodes: 90);
        return BackgroundTextMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadText(DependencyObject root, int maximumNodes)
    {
        var values = new List<string>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var visited = 0;

        while (queue.Count > 0 && visited++ < maximumNodes)
        {
            var current = queue.Dequeue();
            switch (current)
            {
                case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                    values.Add(textBlock.Text);
                    break;
                case ContentControl control when control.Content is string text && !string.IsNullOrWhiteSpace(text):
                    values.Add(text);
                    break;
            }

            if (current is not Visual and not System.Windows.Media.Media3D.Visual3D)
                continue;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
        }

        return string.Join(' ', values);
    }

    private static bool MarkOptimized(FrameworkElement element)
    {
        if (Optimized.TryGetValue(element, out _))
            return false;

        Optimized.Add(element, new object());
        return true;
    }

    private static void Retire(FrameworkElement element)
    {
        if (element.Tag?.ToString() is string tag &&
            tag.Contains("PrimaryWorkActionBar", StringComparison.OrdinalIgnoreCase))
            return;

        element.ClearValue(UIElement.VisibilityProperty);
        element.Visibility = Visibility.Collapsed;
        element.IsHitTestVisible = false;
        element.Focusable = false;

        if (element is Popup popup)
        {
            popup.IsOpen = false;
            return;
        }

        var parent = element.Parent ?? VisualTreeHelper.GetParent(element);
        switch (parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
        }
    }

    private sealed class WindowState(MainWindow window)
    {
        private bool _cleanupPending;
        private bool _disposed;

        public void ScheduleCleanup()
        {
            if (_disposed || _cleanupPending)
                return;

            _cleanupPending = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _cleanupPending = false;
                if (_disposed || !window.IsLoaded)
                    return;

                CleanupVisibleBackgroundSurfaces(window);
            }));
        }

        public void OnContentRendered(object? sender, EventArgs e) => ScheduleCleanup();

        public void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            window.ContentRendered -= OnContentRendered;
            window.Closed -= OnClosed;
        }
    }

    private static void CleanupVisibleBackgroundSurfaces(DependencyObject root)
    {
        var candidates = new List<FrameworkElement>();
        CollectCandidates(root, candidates, maximumNodes: 2200);

        foreach (var candidate in candidates)
        {
            if (LooksLikeBackgroundSurface(candidate))
                Retire(candidate);
        }
    }

    private static void CollectCandidates(DependencyObject root, ICollection<FrameworkElement> results, int maximumNodes)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var visited = 0;

        while (queue.Count > 0 && visited++ < maximumNodes)
        {
            var current = queue.Dequeue();
            if (current is FrameworkElement element &&
                element.Visibility == Visibility.Visible &&
                element is Border or ContentControl or Popup)
                results.Add(element);

            if (current is not Visual and not System.Windows.Media.Media3D.Visual3D)
                continue;

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
        }
    }
}
