using System.Windows;

namespace AIWordPressManager.Desktop;

internal static class Selector
{
    internal static RoutedEvent SelectionChangedEvent =>
        System.Windows.Controls.Primitives.Selector.SelectionChangedEvent;
}
