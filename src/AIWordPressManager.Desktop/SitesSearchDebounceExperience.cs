using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the Sites search responsive by delaying source updates until the user pauses
/// typing briefly. This prevents FilteredSites from being cleared and rebuilt for every
/// individual keystroke while preserving the existing ViewModel and commands.
/// </summary>
internal static class SitesSearchDebounceExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            handledEventsToo: true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => ApplySearchDelay(window)));
    }

    private static void ApplySearchDelay(DependencyObject root)
    {
        foreach (var textBox in Enumerate<TextBox>(root))
        {
            var existing = BindingOperations.GetBinding(textBox, TextBox.TextProperty);
            if (!string.Equals(existing?.Path?.Path, "Sites.SearchText", StringComparison.Ordinal))
                continue;

            var delayed = new Binding("Sites.SearchText")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Delay = 320,
                ValidatesOnDataErrors = existing.ValidatesOnDataErrors,
                ValidatesOnExceptions = existing.ValidatesOnExceptions,
                NotifyOnValidationError = existing.NotifyOnValidationError,
                TargetNullValue = existing.TargetNullValue,
                FallbackValue = existing.FallbackValue
            };

            BindingOperations.SetBinding(textBox, TextBox.TextProperty, delayed);
            textBox.ToolTip = "Search by site name, URL, host or connection status. Results update after you pause typing.";
            break;
        }
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child))
                yield return nested;
        }
    }
}
