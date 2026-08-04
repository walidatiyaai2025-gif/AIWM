using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps page content readable by suppressing legacy always-on floating surfaces.
/// The same destinations remain available through normal navigation and shortcuts.
/// </summary>
internal static class FocusLayoutExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly string[] LegacyFloatingHeadings =
    [
        "Live operations",
        "AI Copilot Inbox",
        "approved change(s) ready for execution",
        "approved changes ready for execution"
    ];

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
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var state = new State(window, root, main);
        Attached.Add(window, state);

        window.PreviewKeyDown += async (_, args) =>
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;

            if (args.Key == Key.O)
            {
                args.Handled = true;
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                return;
            }

            if (args.Key == Key.I)
            {
                args.Handled = true;
                await main.NavigateCommand.ExecuteAsync("Notifications");
            }
        };

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };
        timer.Tick += (_, _) => ApplyFocusLayout(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        ApplyFocusLayout(state);
    }

    private static void ApplyFocusLayout(State state)
    {
        foreach (var candidate in Enumerate<FrameworkElement>(state.Root))
        {
            if (candidate.Tag is "FloatingWorkspaceScrim" or "FloatingWorkspaceLauncher") continue;
            if (IsInsideManagedModal(candidate)) continue;

            var heading = ReadSurfaceHeading(candidate);
            if (heading is null || !IsLegacyFloatingHeading(heading)) continue;

            var surface = FindSurface(candidate);
            if (surface is null || IsInsideManagedModal(surface)) continue;

            surface.Visibility = Visibility.Collapsed;
            surface.IsHitTestVisible = false;
            Panel.SetZIndex(surface, -1);
        }
    }

    private static bool IsLegacyFloatingHeading(string value)
    {
        var normalized = value.Trim();
        return LegacyFloatingHeadings.Any(x =>
            normalized.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadSurfaceHeading(FrameworkElement element)
    {
        if (element is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
            return text.Text;

        if (element is ContentControl content && content.Content is string value && !string.IsNullOrWhiteSpace(value))
            return value;

        return null;
    }

    private static FrameworkElement? FindSurface(DependencyObject origin)
    {
        DependencyObject? current = origin;
        FrameworkElement? fallback = origin as FrameworkElement;

        while (current is not null)
        {
            if (current is Border border)
                return border;

            if (current is Popup)
                return fallback;

            if (current is FrameworkElement element)
                fallback = element;

            current = VisualTreeHelper.GetParent(current);
        }

        return fallback;
    }

    private static bool IsInsideManagedModal(DependencyObject origin)
    {
        DependencyObject? current = origin;
        while (current is not null)
        {
            if (current is FrameworkElement element)
            {
                var tag = element.Tag?.ToString();
                if (tag is "FloatingWorkspaceScrim" or "FloatingWorkspaceLauncher") return true;
                if (tag is not null &&
                    (tag.EndsWith("Panel", StringComparison.Ordinal) ||
                     tag.StartsWith("Close:", StringComparison.Ordinal)))
                {
                    // Managed panels are controlled by FloatingWorkspaceManager and must
                    // not be suppressed when the user explicitly opens one.
                    return tag is "PriorityResolutionPanel"
                        or "ReviewWorkbenchesPanel"
                        or "ContentQualityBatchPanel"
                        or "QuickFixJourneyPanel"
                        or "MediaAnalysisPanel";
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;

            foreach (var nested in Enumerate<T>(child))
                yield return nested;
        }
    }

    private sealed record State(MainWindow Window, Grid Root, MainWindowViewModel Main);
}
