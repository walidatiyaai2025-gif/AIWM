using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

internal static class DemoDataDatabaseActionExperience
{
    private const string AddButtonTag = "AddDemoDataToDatabase";

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DemoDataProgressWindow window)
            AddExplicitDatabaseButton(window);
    }

    private static void AddExplicitDatabaseButton(DemoDataProgressWindow window)
    {
        var buttons = FindVisualChildren<Button>(window).ToArray();
        if (buttons.Any(button => Equals(button.Tag, AddButtonTag)))
            return;

        var refreshButton = buttons.FirstOrDefault(button =>
            button.Content?.ToString()?.Contains("Create / Refresh Demo Data", StringComparison.OrdinalIgnoreCase) == true ||
            button.Content?.ToString()?.Contains("Refresh / Rebuild Demo Data", StringComparison.OrdinalIgnoreCase) == true);

        if (refreshButton is null || VisualTreeHelper.GetParent(refreshButton) is not Grid actions)
            return;

        refreshButton.Content = "Refresh / Rebuild Demo Data";
        refreshButton.ToolTip = "Deletes the current demo rows and rebuilds the complete demo dataset.";
        refreshButton.IsDefault = false;

        var currentColumn = Math.Max(0, Grid.GetColumn(refreshButton));
        actions.ColumnDefinitions.Insert(currentColumn, new ColumnDefinition { Width = GridLength.Auto });

        foreach (var child in actions.Children.OfType<FrameworkElement>().ToArray())
        {
            var column = Grid.GetColumn(child);
            if (child != refreshButton && column >= currentColumn)
                Grid.SetColumn(child, column + 1);
        }

        Grid.SetColumn(refreshButton, currentColumn + 1);

        var addButton = new Button
        {
            Tag = AddButtonTag,
            Content = "Add Demo Data to Database",
            ToolTip = "Writes the configured demo records to the active SQLite database with live progress and transaction logging.",
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(0, 0, 10, 0),
            FontWeight = FontWeights.Bold,
            IsDefault = true
        };

        addButton.Click += (_, _) =>
        {
            var result = MessageBox.Show(
                window,
                "This will write the complete demo dataset to the active local SQLite database.\n\n" +
                "At least 100 records will be written for every configured demo table. " +
                "The progress bar and log will show every successful database operation.\n\nContinue?",
                "Add Demo Data to Database",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
                refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, refreshButton));
        };

        Grid.SetColumn(addButton, currentColumn);
        actions.Children.Add(addButton);
        Panel.SetZIndex(addButton, 10);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
