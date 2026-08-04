using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

public partial class MainWindow
{
    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        var footer = FindFooterTextBlock(window);
        if (footer is null)
            return;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.3.9";

        footer.Text = string.Empty;
        footer.Inlines.Clear();
        footer.Inlines.Add(new Run("AI WordPress Website Manager • Offline-first • "));
        footer.Inlines.Add(new Run("Version ") { FontWeight = FontWeights.Bold });
        footer.Inlines.Add(new Run($"v{version}")
        {
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)window.FindResource("PrimaryHoverBrush")
        });
    }

    private static TextBlock? FindFooterTextBlock(DependencyObject parent)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock &&
                textBlock.Text.StartsWith(
                    "AI WordPress Website Manager • Offline-first",
                    StringComparison.Ordinal))
            {
                return textBlock;
            }

            var nested = FindFooterTextBlock(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
