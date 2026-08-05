using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Applies lightweight, one-time WPF virtualization defaults without changing
/// page commands, editing rules, selection behavior, or data bindings.
/// Background-only legacy overlays are retired when they load; no timer and no
/// repeated full-window visual-tree scan is used.
/// </summary>
internal static class ApplicationPerformanceExperience
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> Optimized = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> RetirementScheduled = new();

    private static readonly string[] BackgroundTextMarkers =
    [
        "Real content analysis",
        "Local rules run against the synchronized WordPress snapshot",
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

    private static void OnDataGridLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not DataGrid grid || !MarkOptimized(grid))
            return;

        // Performance-only settings. Do not overwrite IsReadOnly, selection mode,
        // headers, row details, commands, or user-defined scroll-bar behavior.
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        grid.SnapsToDevicePixels = true;
        grid.UseLayoutRounding = true;

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(grid, true);
        VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(grid, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(grid, true);
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ItemsControl items || items is DataGrid || !MarkOptimized(items))
            return;

        VirtualizingPanel.SetIsVirtualizing(items, true);
        VirtualizingPanel.SetVirtualizationMode(items, VirtualizationMode.Recycling);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(items, true);
        VirtualizingPanel.SetScrollUnit(items, ScrollUnit.Pixel);
        ScrollViewer.SetCanContentScroll(items, true);
        ScrollViewer.SetIsDeferredScrollingEnabled(items, true);
    }

    private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer viewer || !MarkOptimized(viewer))
            return;

        // Keep page-specific PanningMode and bar visibility unchanged.
        viewer.IsDeferredScrollingEnabled = true;
        viewer.UseLayoutRounding = true;
        viewer.SnapsToDevicePixels = true;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element || element is MainWindow)
            return;

        if (Window.GetWindow(element) is not MainWindow || !LooksLikeBackgroundSurface(element))
            return;

        if (RetirementScheduled.TryGetValue(element, out _))
            return;

        RetirementScheduled.Add(element, new object());
        _ = element.Dispatcher.BeginInvoke(
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

        // Text checks are limited to plausible overlay containers and a small subtree.
        if (element is not Border and not ContentControl)
            return false;

        if (element.ActualWidth > 0 && element.ActualWidth < 260)
            return false;

        var text = ReadText(element, maximumNodes: 70);
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
        if (!element.IsLoaded)
            return;

        var tag = element.Tag?.ToString() ?? string.Empty;
        if (tag.Contains("PrimaryWorkActionBar", StringComparison.OrdinalIgnoreCase) ||
            tag.Contains("Docked", StringComparison.OrdinalIgnoreCase))
            return;

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
}
