using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIWordPressManager.Desktop.Services;

/// <summary>
/// Keeps the visible shell release identifier synchronized with the executable
/// assembly version without requiring a hard-coded footer version in XAML.
/// </summary>
internal static class VisibleReleaseVersion
{
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
        if (sender is not MainWindow mainWindow)
        {
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is null
            ? "Version 1.40.0 • Part 140"
            : $"Version {version.Major}.{version.Minor}.{version.Build} • Part 140";

        foreach (var textBlock in FindVisualChildren<TextBlock>(mainWindow))
        {
            if (!string.Equals(
                    textBlock.Text,
                    "AI WordPress Website Manager • Offline-first",
                    StringComparison.Ordinal))
            {
                continue;
            }

            textBlock.Text = $"AI WordPress Website Manager • Offline-first • {versionText}";
            textBlock.FontWeight = FontWeights.Bold;
            textBlock.ToolTip = $"Current installed release: {versionText}";
            break;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
