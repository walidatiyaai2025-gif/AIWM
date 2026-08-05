using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps background hydration silent and prevents the legacy operation surface from
/// overlapping the user-initiated UiOperationService progress surface.
/// </summary>
internal static class OperationSurfaceCoordinator
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => RemoveLegacyBackgroundOperationSurface(window)));
    }

    private static void RemoveLegacyBackgroundOperationSurface(DependencyObject root)
    {
        foreach (var border in Enumerate<Border>(root).ToArray())
        {
            var binding = BindingOperations.GetBinding(border, UIElement.VisibilityProperty);
            if (!string.Equals(binding?.Path?.Path, "IsOperationRunning", StringComparison.Ordinal))
                continue;

            border.Visibility = Visibility.Collapsed;
            border.IsHitTestVisible = false;
            border.DataContext = null;
            BindingOperations.ClearAllBindings(border);
            RemoveFromParent(border);
        }
    }

    private static void RemoveFromParent(FrameworkElement element)
    {
        switch (VisualTreeHelper.GetParent(element))
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
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
