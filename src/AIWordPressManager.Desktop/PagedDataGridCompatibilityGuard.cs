using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AIWordPressManager.Desktop.Behaviors;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Prevents the universal paging behavior from attaching to collection views
/// that explicitly do not support filtering. The grid remains fully usable;
/// only the optional client-side paging toolbar is disabled for that source.
/// </summary>
internal static class PagedDataGridCompatibilityGuard
{
    private static readonly ConditionalWeakTable<DataGrid, GridRegistration> Registrations = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnGridLoaded),
            true);
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || !ReferenceEquals(e.OriginalSource, grid))
            return;

        if (!Registrations.TryGetValue(grid, out _))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty,
                typeof(DataGrid));
            var registration = new GridRegistration(grid, descriptor);
            Registrations.Add(grid, registration);
            descriptor?.AddValueChanged(grid, registration.OnItemsSourceChanged);
            grid.Unloaded += registration.OnUnloaded;
        }

        ApplyCompatibility(grid);
    }

    private static void ApplyCompatibility(DataGrid grid)
    {
        if (!PagedDataGridBehavior.GetIsEnabled(grid))
            return;

        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (view is null || view.CanFilter)
            return;

        // This source cannot accept ICollectionView.Filter. Disable only the
        // optional paging behavior and leave the original DataGrid untouched.
        PagedDataGridBehavior.SetIsEnabled(grid, false);
        grid.ToolTip = string.IsNullOrWhiteSpace(grid.ToolTip?.ToString())
            ? "Paging is unavailable for this data source; all rows remain visible."
            : grid.ToolTip;
    }

    private sealed class GridRegistration
    {
        private readonly WeakReference<DataGrid> _grid;
        private readonly DependencyPropertyDescriptor? _descriptor;

        internal GridRegistration(DataGrid grid, DependencyPropertyDescriptor? descriptor)
        {
            _grid = new WeakReference<DataGrid>(grid);
            _descriptor = descriptor;
        }

        internal void OnItemsSourceChanged(object? sender, EventArgs e)
        {
            if (_grid.TryGetTarget(out var grid))
                ApplyCompatibility(grid);
        }

        internal void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGrid grid || grid.IsLoaded || grid.Parent is not null)
                return;

            _descriptor?.RemoveValueChanged(grid, OnItemsSourceChanged);
            grid.Unloaded -= OnUnloaded;
            Registrations.Remove(grid);
        }
    }
}
