using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class SiteWizardProfessionalLabels
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => Apply(window)));
    }

    private static void Apply(DependencyObject root)
    {
        foreach (var button in FindVisualChildren<Button>(root))
        {
            if (button.Content is string text &&
                text.Equals("Save site", StringComparison.OrdinalIgnoreCase))
            {
                button.Content = "Save & start first sync";
                button.ToolTip = "Save the verified WordPress website, select it, and continue directly to the first synchronization.";
                button.MinWidth = Math.Max(button.MinWidth, 180);
            }
        }

        foreach (var block in FindVisualChildren<TextBlock>(root))
        {
            if (block.Text.Equals("Complete discovery and safety preferences before saving.", StringComparison.OrdinalIgnoreCase))
                block.Text = "Four clear steps: website, credentials, connection test, then save and start synchronization.";
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
