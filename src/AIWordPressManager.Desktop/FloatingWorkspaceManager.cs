using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class FloatingWorkspaceManager
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly (string Tag, string Label)[] ManagedPanels =
    [
        ("PriorityResolutionPanel", "Priority"),
        ("ReviewWorkbenchesPanel", "Review"),
        ("ContentQualityBatchPanel", "Quality"),
        ("QuickFixJourneyPanel", "Journey"),
        ("MediaAnalysisPanel", "Media"),
        ("AiCopilotInboxPanel", "AI Inbox")
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
        if (window.Content is not Grid root) return;

        var state = new State(root);
        Attached.Add(window, state);

        var launcher = BuildLauncher(state);
        Grid.SetRow(launcher, 3);
        Panel.SetZIndex(launcher, 500);
        root.Children.Add(launcher);

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) => Synchronize(state, launcher);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Synchronize(state, launcher);
    }

    private static Border BuildLauncher(State state)
    {
        var shell = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 18, 18),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(12),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "FloatingWorkspaceLauncher"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Workspace tools",
            Margin = new Thickness(4, 0, 4, 6),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });

        var actions = new WrapPanel();
        stack.Children.Add(actions);
        foreach (var managed in ManagedPanels)
        {
            var button = new Button
            {
                Content = managed.Label,
                Tag = $"Launcher:{managed.Tag}",
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(9, 5, 9, 5),
                Visibility = Visibility.Collapsed
            };
            button.Click += (_, _) =>
            {
                state.OpenPanel(managed.Tag);
                Synchronize(state, shell);
            };
            actions.Children.Add(button);
        }
        return shell;
    }

    private static void Synchronize(State state, Border launcher)
    {
        var panels = ManagedPanels
            .Select(x => (Definition: x, Panel: FindByTag<Border>(state.Root, x.Tag)))
            .Where(x => x.Panel is not null)
            .Select(x => (x.Definition, Panel: x.Panel!))
            .ToArray();

        foreach (var item in panels)
            EnsureCloseButton(item.Panel, item.Definition.Tag, state);

        var naturallyVisible = panels
            .Where(x => x.Panel.Visibility == Visibility.Visible && !state.Dismissed.Contains(x.Definition.Tag))
            .ToArray();

        if (state.ActiveTag is not null)
        {
            var active = panels.FirstOrDefault(x => x.Definition.Tag == state.ActiveTag);
            if (active.Panel is null)
                state.ActiveTag = null;
        }

        if (state.ActiveTag is null && naturallyVisible.Length > 0)
            state.ActiveTag = naturallyVisible[0].Definition.Tag;

        foreach (var item in panels)
        {
            var shouldShow = item.Definition.Tag == state.ActiveTag && !state.Dismissed.Contains(item.Definition.Tag);
            if (!shouldShow)
                item.Panel.Visibility = Visibility.Collapsed;
        }

        var anyLauncherButton = false;
        foreach (var managed in ManagedPanels)
        {
            var button = FindByTag<Button>(launcher, $"Launcher:{managed.Tag}");
            if (button is null) continue;
            var exists = panels.Any(x => x.Definition.Tag == managed.Tag);
            var canOpen = exists && (state.Dismissed.Contains(managed.Tag) || state.ActiveTag != managed.Tag);
            button.Visibility = canOpen ? Visibility.Visible : Visibility.Collapsed;
            anyLauncherButton |= canOpen;
        }
        launcher.Visibility = anyLauncherButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void EnsureCloseButton(Border panel, string tag, State state)
    {
        if (panel.Child is not StackPanel stack) return;
        if (FindByTag<Button>(stack, $"Close:{tag}") is not null) return;

        var close = new Button
        {
            Content = "×",
            Tag = $"Close:{tag}",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, -8, -8, 2),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            ToolTip = "Close this workspace panel"
        };
        close.Click += (_, _) =>
        {
            state.Dismissed.Add(tag);
            if (state.ActiveTag == tag) state.ActiveTag = null;
            panel.Visibility = Visibility.Collapsed;
        };
        stack.Children.Insert(0, close);
    }

    private sealed class State(Grid root)
    {
        public Grid Root { get; } = root;
        public HashSet<string> Dismissed { get; } = new(StringComparer.Ordinal);
        public string? ActiveTag { get; set; }

        public void OpenPanel(string tag)
        {
            Dismissed.Remove(tag);
            ActiveTag = tag;
            foreach (var managed in ManagedPanels)
            {
                var panel = FindByTag<Border>(Root, managed.Tag);
                if (panel is null) continue;
                panel.Visibility = managed.Tag == tag ? Visibility.Visible : Visibility.Collapsed;
            }
        }
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

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
