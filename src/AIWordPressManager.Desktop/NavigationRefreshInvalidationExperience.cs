using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Invalidates the short navigation-load freshness window before an explicit user
/// refresh. Normal navigation can reuse recent data, while buttons and Ctrl+Shift+R
/// continue to force the existing RefreshCurrentPageCommand load path.
/// </summary>
internal static class NavigationRefreshInvalidationExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown),
            true);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            window.DataContext is not MainWindowViewModel main)
            return;

        if (ReferenceEquals(button.Command, main.RefreshCurrentPageCommand))
            main.InvalidateNavigationLoadCache(main.CurrentPage);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window ||
            window.DataContext is not MainWindowViewModel main)
            return;

        if (e.Key == Key.R &&
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) ==
            (ModifierKeys.Control | ModifierKeys.Shift))
        {
            main.InvalidateNavigationLoadCache(main.CurrentPage);
        }
    }
}
