using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Applies safe WPF virtualization and recycling to collection controls as they load.
/// Explicit screen-level scrolling and row-detail settings are preserved.
/// </summary>
internal static class VirtualizedCollectionExperience
{
    private static readonly ConditionalWeakTable<ItemsControl, object> Configured = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(ItemsControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnItemsControlLoaded),
            true);
    }

    private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl control || !ReferenceEquals(e.OriginalSource, control)) return;
        if (Configured.TryGetValue(control, out _)) return;

        Configured.Add(control, new object());

        VirtualizingPanel.SetIsVirtualizing(control, true);
        VirtualizingPanel.SetVirtualizationMode(control, VirtualizationMode.Recycling);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(control, true);
        VirtualizingPanel.SetCacheLengthUnit(control, VirtualizationCacheLengthUnit.Page);
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(1, 1));
        ScrollViewer.SetIsDeferredScrollingEnabled(control, true);
        ScrollViewer.SetCanContentScroll(control, true);

        switch (control)
        {
            case DataGrid grid:
                grid.EnableRowVirtualization = true;
                grid.EnableColumnVirtualization = true;
                EnsureAutomaticScrollBars(grid);
                break;

            // ListView derives from ListBox, so it must be matched first.
            case ListView listView:
                EnsureAutomaticScrollBars(listView);
                break;

            case ListBox listBox:
                EnsureAutomaticScrollBars(listBox);
                break;

            case TreeView treeView:
                EnsureAutomaticScrollBars(treeView);
                break;
        }
    }

    private static void EnsureAutomaticScrollBars(DependencyObject control)
    {
        if (ScrollViewer.GetVerticalScrollBarVisibility(control) == ScrollBarVisibility.Disabled)
            ScrollViewer.SetVerticalScrollBarVisibility(control, ScrollBarVisibility.Auto);

        if (ScrollViewer.GetHorizontalScrollBarVisibility(control) == ScrollBarVisibility.Visible)
            ScrollViewer.SetHorizontalScrollBarVisibility(control, ScrollBarVisibility.Auto);
    }
}
