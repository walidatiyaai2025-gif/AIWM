using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Routes the former floating-panel shortcuts to the docked right sidebar.
/// No timer or polling is used; controls are resolved only when a shortcut is pressed.
/// </summary>
internal static class DockedSidebarShortcutExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

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
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;

        Attached.Add(window, new object());
        window.PreviewKeyDown += OnPreviewKeyDown;
        window.Closed += OnClosed;
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window) return;

        if (e.Key == Key.Escape && TryCloseSidebar(window))
        {
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
            (ModifierKeys.Control | ModifierKeys.Shift)) return;

        var label = e.Key switch
        {
            Key.N => "Notifications",
            Key.J => "Journey",
            Key.O => "Operations",
            Key.A => "AI Copilot",
            Key.Q => "Quick Fix",
            _ => null
        };
        if (label is null) return;

        var sidebar = FindByTag<Border>(window, "DockedRightSidebar");
        var button = sidebar is null
            ? null
            : Enumerate<Button>(sidebar)
                .FirstOrDefault(candidate => string.Equals(
                    candidate.ToolTip?.ToString(), label, StringComparison.OrdinalIgnoreCase));

        if (button is null || !button.IsEnabled) return;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        e.Handled = true;
    }

    private static bool TryCloseSidebar(MainWindow window)
    {
        var host = FindByTag<Grid>(window, "DockedWorkspaceHost");
        var sidebar = FindByTag<Border>(window, "DockedRightSidebar");
        if (host is null || sidebar is null || host.ColumnDefinitions.Count < 2) return false;
        if (host.ColumnDefinitions[1].Width.Value <= 44.5) return false;

        var close = Enumerate<Button>(sidebar)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "✕", StringComparison.Ordinal));
        if (close is null) return false;

        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));
        return true;
    }

    private static void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not MainWindow window) return;
        window.PreviewKeyDown -= OnPreviewKeyDown;
        window.Closed -= OnClosed;
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && string.Equals(match.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            return match;

        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var found = FindByTag<T>(child, tag);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T current) yield return current;
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }
}
