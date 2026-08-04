using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class FloatingWorkspaceManager
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly (string Tag, string Label)[] ManagedPanels =
    [
        ("PriorityResolutionPanel", "Priority resolution"),
        ("ReviewWorkbenchesPanel", "Review workbenches"),
        ("ContentQualityBatchPanel", "Content quality"),
        ("QuickFixJourneyPanel", "Journey and quick fixes"),
        ("MediaAnalysisPanel", "Media analysis"),
        ("AiCopilotInboxPanel", "AI Copilot inbox")
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

        var state = new State(window, root);
        Attached.Add(window, state);

        state.Scrim = BuildScrim(state);
        Grid.SetRow(state.Scrim, 3);
        Panel.SetZIndex(state.Scrim, 900);
        root.Children.Add(state.Scrim);

        state.Launcher = BuildLauncher(state);
        Grid.SetRow(state.Launcher, 3);
        Panel.SetZIndex(state.Launcher, 850);
        root.Children.Add(state.Launcher);

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape || state.ActiveTag is null) return;
            state.CloseActivePanel();
            args.Handled = true;
        };

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        timer.Tick += (_, _) => Synchronize(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Synchronize(state);
    }

    private static Border BuildScrim(State state)
    {
        var scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = true,
            Tag = "FloatingWorkspaceScrim"
        };
        scrim.MouseLeftButtonDown += (_, args) =>
        {
            state.CloseActivePanel();
            args.Handled = true;
        };
        return scrim;
    }

    private static Button BuildLauncher(State state)
    {
        var button = new Button
        {
            Content = "Workspace tools ▾",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 16, 0),
            Padding = new Thickness(12, 7, 12, 7),
            MinWidth = 138,
            Visibility = Visibility.Collapsed,
            Tag = "FloatingWorkspaceLauncher",
            ToolTip = "Open analysis and review tools"
        };

        button.Click += (_, _) =>
        {
            RebuildContextMenu(state, button);
            if (button.ContextMenu is null || button.ContextMenu.Items.Count == 0) return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        return button;
    }

    private static void RebuildContextMenu(State state, Button launcher)
    {
        var menu = new ContextMenu();
        foreach (var managed in ManagedPanels)
        {
            if (FindByTag<Border>(state.Root, managed.Tag) is null) continue;
            var item = new MenuItem
            {
                Header = managed.Label,
                Tag = managed.Tag,
                Padding = new Thickness(12, 7, 18, 7)
            };
            item.Click += (_, _) => state.OpenPanel(managed.Tag);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
            var close = new MenuItem
            {
                Header = "Close workspace",
                IsEnabled = state.ActiveTag is not null,
                Padding = new Thickness(12, 7, 18, 7)
            };
            close.Click += (_, _) => state.CloseActivePanel();
            menu.Items.Add(close);
        }
        launcher.ContextMenu = menu;
    }

    private static void Synchronize(State state)
    {
        var panels = ManagedPanels
            .Select(x => (Definition: x, Panel: FindByTag<Border>(state.Root, x.Tag)))
            .Where(x => x.Panel is not null)
            .Select(x => (x.Definition, Panel: x.Panel!))
            .ToArray();

        if (state.Launcher is not null)
            state.Launcher.Visibility = panels.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in panels)
        {
            PreparePanel(item.Panel, item.Definition.Tag, state);
            var shouldShow = state.ActiveTag == item.Definition.Tag;
            item.Panel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            item.Panel.IsHitTestVisible = shouldShow;
        }

        if (state.ActiveTag is not null && panels.All(x => x.Definition.Tag != state.ActiveTag))
            state.ActiveTag = null;

        if (state.Scrim is not null)
        {
            state.Scrim.Visibility = state.ActiveTag is null ? Visibility.Collapsed : Visibility.Visible;
            state.Scrim.IsHitTestVisible = state.ActiveTag is not null;
        }
    }

    private static void PreparePanel(Border panel, string tag, State state)
    {
        panel.HorizontalAlignment = HorizontalAlignment.Right;
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.Margin = new Thickness(20, 60, 28, 20);
        panel.MaxWidth = 560;
        panel.MaxHeight = 680;
        panel.IsHitTestVisible = false;
        Panel.SetZIndex(panel, 920);

        if (panel.Child is not StackPanel) return;
        if (FindByTag<Button>(panel, $"Close:{tag}") is not null) return;

        var content = panel.Child;
        panel.Child = null;

        var dock = new DockPanel { LastChildFill = true };
        var close = new Button
        {
            Content = "×",
            Tag = $"Close:{tag}",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, -8, -8, 6),
            Padding = new Thickness(9, 2, 9, 2),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            ToolTip = "Close (Esc)"
        };
        DockPanel.SetDock(close, Dock.Top);
        close.Click += (_, args) =>
        {
            state.CloseActivePanel();
            args.Handled = true;
        };
        dock.Children.Add(close);

        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
            MaxHeight = 620
        };
        dock.Children.Add(viewer);
        panel.Child = dock;

        panel.MouseLeftButtonDown += (_, args) => args.Handled = true;
    }

    private sealed class State(MainWindow window, Grid root)
    {
        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public Border? Scrim { get; set; }
        public Button? Launcher { get; set; }
        public string? ActiveTag { get; set; }

        public void OpenPanel(string tag)
        {
            ActiveTag = tag;
            Synchronize(this);
        }

        public void CloseActivePanel()
        {
            ActiveTag = null;
            Synchronize(this);
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
}
