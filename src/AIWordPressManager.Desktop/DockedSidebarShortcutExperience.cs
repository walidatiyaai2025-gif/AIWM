using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Routes workspace shortcuts to the docked right sidebar using cached controls.
/// Visual-tree discovery happens once after the sidebar is loaded, not per key press.
/// </summary>
internal static class DockedSidebarShortcutExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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

        var state = new State(window);
        Attached.Add(window, state);
        window.PreviewKeyDown += state.OnPreviewKeyDown;
        window.Closed += state.OnClosed;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(state.ResolveControls));
    }

    private sealed class State(MainWindow window)
    {
        private readonly Dictionary<string, Button> _sectionButtons =
            new(StringComparer.OrdinalIgnoreCase);

        private Grid? _host;
        private Border? _sidebar;
        private Button? _closeButton;
        private bool _resolved;

        public void ResolveControls()
        {
            if (_resolved || !window.IsLoaded) return;

            _host = FindByTag<Grid>(window, "DockedWorkspaceHost");
            _sidebar = FindByTag<Border>(window, "DockedRightSidebar");
            if (_host is null || _sidebar is null) return;

            _sectionButtons.Clear();
            foreach (var button in Enumerate<Button>(_sidebar))
            {
                var toolTip = button.ToolTip?.ToString();
                if (!string.IsNullOrWhiteSpace(toolTip) &&
                    toolTip is "Notifications" or "Journey" or "Operations" or "AI Copilot" or "Quick Fix")
                {
                    _sectionButtons[toolTip] = button;
                }

                if (string.Equals(button.Content?.ToString(), "✕", StringComparison.Ordinal))
                    _closeButton = button;
            }

            _resolved = _sectionButtons.Count > 0;
        }

        public void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_resolved) ResolveControls();

            if (e.Key == Key.Escape && TryCloseSidebar())
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

            if (!_sectionButtons.TryGetValue(label, out var button) || !button.IsEnabled)
            {
                _resolved = false;
                ResolveControls();
                if (!_sectionButtons.TryGetValue(label, out button) || !button.IsEnabled) return;
            }

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            e.Handled = true;
        }

        private bool TryCloseSidebar()
        {
            if (_host is null || _sidebar is null || _host.ColumnDefinitions.Count < 2) return false;
            if (_host.ColumnDefinitions[1].Width.Value <= 44.5) return false;

            if (_closeButton is null || !_closeButton.IsEnabled)
            {
                _closeButton = Enumerate<Button>(_sidebar)
                    .FirstOrDefault(button => string.Equals(
                        button.Content?.ToString(), "✕", StringComparison.Ordinal));
            }

            if (_closeButton is null) return false;
            _closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, _closeButton));
            return true;
        }

        public void OnClosed(object? sender, EventArgs e)
        {
            window.PreviewKeyDown -= OnPreviewKeyDown;
            window.Closed -= OnClosed;
            _sectionButtons.Clear();
            _host = null;
            _sidebar = null;
            _closeButton = null;
            _resolved = false;
        }
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
