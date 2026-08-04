using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace AIWordPressManager.Desktop.Behaviors;

public static class DataGridSelectedItemsBehavior
{
    private static readonly DependencyProperty IsSynchronizingProperty = DependencyProperty.RegisterAttached(
        "IsSynchronizing", typeof(bool), typeof(DataGridSelectedItemsBehavior), new PropertyMetadata(false));

    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.RegisterAttached(
        "SelectedItems",
        typeof(IList),
        typeof(DataGridSelectedItemsBehavior),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.None,
            OnSelectedItemsChanged));

    public static void SetSelectedItems(DependencyObject element, IList value) => element.SetValue(SelectedItemsProperty, value);
    public static IList? GetSelectedItems(DependencyObject element) => (IList?)element.GetValue(SelectedItemsProperty);

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;

        grid.SelectionChanged -= GridOnSelectionChanged;
        grid.Loaded -= GridOnLoaded;

        if (e.OldValue is INotifyCollectionChanged oldCollection &&
            grid.GetValue(CollectionHandlerProperty) is NotifyCollectionChangedEventHandler oldHandler)
        {
            oldCollection.CollectionChanged -= oldHandler;
            grid.ClearValue(CollectionHandlerProperty);
        }

        if (e.NewValue is IList)
        {
            grid.SelectionChanged += GridOnSelectionChanged;
            grid.Loaded += GridOnLoaded;
            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                // Store a stable handler on the grid so programmatic selection is reflected visually.
                NotifyCollectionChangedEventHandler handler = (_, _) => SyncGridFromSource(grid);
                grid.SetValue(CollectionHandlerProperty, handler);
                newCollection.CollectionChanged += handler;
            }
            SyncGridFromSource(grid);
        }
    }

    private static readonly DependencyProperty CollectionHandlerProperty = DependencyProperty.RegisterAttached(
        "CollectionHandler", typeof(NotifyCollectionChangedEventHandler), typeof(DataGridSelectedItemsBehavior));

    private static void GridOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid) SyncGridFromSource(grid);
    }

    private static void GridOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid || GetSelectedItems(grid) is not IList target || GetIsSynchronizing(grid)) return;
        SetIsSynchronizing(grid, true);
        try
        {
            target.Clear();
            foreach (var item in grid.SelectedItems) target.Add(item);
        }
        finally { SetIsSynchronizing(grid, false); }
    }

    private static void SyncGridFromSource(DataGrid grid)
    {
        if (GetSelectedItems(grid) is not IList source || GetIsSynchronizing(grid)) return;
        SetIsSynchronizing(grid, true);
        try
        {
            grid.SelectedItems.Clear();
            foreach (var item in source)
                if (grid.Items.Contains(item)) grid.SelectedItems.Add(item);
        }
        finally { SetIsSynchronizing(grid, false); }
    }

    private static bool GetIsSynchronizing(DependencyObject obj) => (bool)obj.GetValue(IsSynchronizingProperty);
    private static void SetIsSynchronizing(DependencyObject obj, bool value) => obj.SetValue(IsSynchronizingProperty, value);
}
