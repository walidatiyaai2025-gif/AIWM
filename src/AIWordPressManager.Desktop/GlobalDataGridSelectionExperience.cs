using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

internal static class GlobalDataGridSelectionExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDataGridLoaded));

        EventManager.RegisterClassHandler(
            typeof(CheckBox),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnCheckBoxPreviewMouseLeftButtonDown),
            true);

        EventManager.RegisterClassHandler(
            typeof(CheckBox),
            ToggleButton.CheckedEvent,
            new RoutedEventHandler(OnCheckBoxValueChanged),
            true);

        EventManager.RegisterClassHandler(
            typeof(CheckBox),
            ToggleButton.UncheckedEvent,
            new RoutedEventHandler(OnCheckBoxValueChanged),
            true);
    }

    private static void OnDataGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grid || !ReferenceEquals(e.OriginalSource, grid))
            return;

        // Standardize selection behavior without replacing screen-specific bindings.
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.CanUserAddRows = false;
        grid.HeadersVisibility = DataGridHeadersVisibility.All;
    }

    private static void OnCheckBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        var row = FindAncestor<DataGridRow>(checkBox);
        var grid = row is null ? null : FindAncestor<DataGrid>(row);
        if (row is null || grid is null)
            return;

        // A checkbox click must also establish the row as the active selection.
        // Ctrl keeps additive selection; a normal click makes this the current row.
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 && !row.IsSelected)
            grid.SelectedItems.Clear();

        row.IsSelected = true;
        grid.SelectedItem = row.Item;
        grid.CurrentCell = new DataGridCellInfo(row.Item, grid.CurrentColumn ?? grid.Columns.FirstOrDefault());
        row.Focus();
    }

    private static void OnCheckBoxValueChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        // Commit TwoWay and explicit bindings immediately so toolbar counts and commands
        // react on the first click instead of waiting for focus to leave the cell.
        checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();

        var row = FindAncestor<DataGridRow>(checkBox);
        var grid = row is null ? null : FindAncestor<DataGrid>(row);
        if (row is null || grid is null)
            return;

        row.IsSelected = true;
        grid.SelectedItem = row.Item;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
