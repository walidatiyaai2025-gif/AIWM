using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Enforces the work-page layout contract:
/// page content, one docked action bar and no persistent overlay cards.
/// </summary>
internal static class WorkPageOverlayGuard
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly string[] BlockedTextMarkers =
    [
        "approved change(s) ready for execution",
        "approved changes ready for execution",
        "Guided workspace",
        "Journey completion",
        "Quick Fix Queue",
        "Priority resolution workspace",
        "Review workbenches",
        "AI Copilot Inbox",
        "Live operations"
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

        var state = new State(root, main);
        Attached.Add(window, state);

        var timer = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        timer.Tick += (_, _) => Apply(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Apply(state);
    }

    private static void Apply(State state)
    {
        SuppressBlockedSurfaces(state.Root);
        EnsureCompactExecutionStatus(state);
    }

    private static void SuppressBlockedSurfaces(DependencyObject root)
    {
        foreach (var element in Enumerate<FrameworkElement>(root).ToArray())
        {
            if (element.Tag?.ToString() is string tag &&
                (tag.Contains("Panel", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("Workspace", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("Overlay", StringComparison.OrdinalIgnoreCase)))
            {
                if (tag is "PrimaryWorkActionBar" or "ProfessionalStatusBar")
                    continue;

                if (tag.Contains("PriorityResolution", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("ReviewWorkbenches", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("QuickFixJourney", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("ContentQualityBatch", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("MediaAnalysis", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("AiCopilotInbox", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("FloatingWorkspace", StringComparison.OrdinalIgnoreCase))
                {
                    Collapse(element);
                    continue;
                }
            }

            if (element is Border or ContentControl)
            {
                var text = ReadDescendantText(element);
                if (BlockedTextMarkers.Any(marker =>
                        text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    Collapse(element);
                }
            }
        }
    }

    private static void EnsureCompactExecutionStatus(State state)
    {
        var actionBar = FindByTag<Border>(state.Root, "PrimaryWorkActionBar");
        if (actionBar?.Child is not Grid grid) return;

        var status = FindByTag<Button>(actionBar, "CompactExecutionStatus");
        if (status is null)
        {
            status = new Button
            {
                Tag = "CompactExecutionStatus",
                Height = 24,
                MinWidth = 126,
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(9, 2, 9, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Open Execution Center",
                HorizontalAlignment = HorizontalAlignment.Right
            };
            status.Click += async (_, _) =>
                await state.Main.NavigateCommand.ExecuteAsync("Execution Center");

            Grid.SetColumn(status, 1);
            grid.Children.Add(status);
        }

        var ready = state.Main.ExecutionCenter.ReadyCount;
        var failed = state.Main.ExecutionCenter.FailedCount;
        status.Content = failed > 0
            ? $"Execution: {ready} ready • {failed} failed"
            : $"Execution: {ready} ready";
        status.Visibility = ready > 0 || failed > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        status.IsHitTestVisible = status.Visibility == Visibility.Visible;
    }

    private static void Collapse(FrameworkElement element)
    {
        element.Visibility = Visibility.Collapsed;
        element.IsHitTestVisible = false;
        element.Focusable = false;
        Panel.SetZIndex(element, -1000);
    }

    private static string ReadDescendantText(DependencyObject root)
    {
        var values = new List<string>();
        foreach (var element in Enumerate<DependencyObject>(root))
        {
            switch (element)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    values.Add(text.Text);
                    break;
                case ContentControl control when control.Content is string value && !string.IsNullOrWhiteSpace(value):
                    values.Add(value);
                    break;
            }
        }
        return string.Join(" ", values);
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindByTag<T>(child, tag);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private sealed class State(Grid root, MainWindowViewModel main)
    {
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
    }
}
