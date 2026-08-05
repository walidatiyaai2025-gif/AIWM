using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Applies WPF virtualization and recycling to collection controls as they are loaded.
/// This keeps large grids and lists responsive without changing their view models.
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
                grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
                ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
                ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
                break;

            case ListBox listBox:
                ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
                if (ScrollViewer.GetHorizontalScrollBarVisibility(listBox) == ScrollBarVisibility.Visible)
                    ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
                break;

            case ListView listView:
                ScrollViewer.SetVerticalScrollBarVisibility(listView, ScrollBarVisibility.Auto);
                if (ScrollViewer.GetHorizontalScrollBarVisibility(listView) == ScrollBarVisibility.Visible)
                    ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Auto);
                break;

            case TreeView treeView:
                ScrollViewer.SetVerticalScrollBarVisibility(treeView, ScrollBarVisibility.Auto);
                break;
        }
    }
}
