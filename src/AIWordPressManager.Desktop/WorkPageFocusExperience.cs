using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class WorkPageFocusExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly HashSet<string> NonWorkPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard",
        "Sites",
        "Help",
        "Settings",
        "Notification Center"
    };

    private static readonly string[] AuxiliaryPanelTags =
    [
        "PriorityResolutionPanel",
        "ReviewWorkbenchesPanel",
        "ContentQualityBatchPanel",
        "QuickFixJourneyPanel",
        "MediaAnalysisPanel",
        "AiCopilotInboxPanel"
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

        state.ActionBar = BuildPrimaryActionBar(state);
        Grid.SetRow(state.ActionBar, 2);
        Panel.SetZIndex(state.ActionBar, 120);

        foreach (var child in root.Children.OfType<FrameworkElement>()
                     .Where(x => Grid.GetRow(x) == 2)
                     .ToArray())
        {
            child.Visibility = Visibility.Collapsed;
            child.IsHitTestVisible = false;
        }

        root.Children.Add(state.ActionBar);

        main.PropertyChanged += (_, _) => Apply(state);
        main.Sites.PropertyChanged += (_, _) => Apply(state);
        main.SuggestedChanges.PropertyChanged += (_, _) => Apply(state);
        main.ExecutionCenter.PropertyChanged += (_, _) => Apply(state);
        main.Explorer.PropertyChanged += (_, _) => Apply(state);
        main.SuggestedChanges.Items.CollectionChanged += (_, _) => Apply(state);
        main.ExecutionCenter.Items.CollectionChanged += (_, _) => Apply(state);

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        timer.Tick += (_, _) => Apply(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Apply(state);
    }

    private static Border BuildPrimaryActionBar(State state)
    {
        var shell = new Border
        {
            Height = 36,
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0, 14, 0),
            Tag = "PrimaryWorkActionBar"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = grid;

        var title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black),
            Margin = new Thickness(0, 0, 12, 0)
        };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.PageTitle)));
        grid.Children.Add(title);

        var context = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(context, 1);
        grid.Children.Add(context);

        state.WorkspaceSummary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "Current website workspace status"
        };
        context.Children.Add(state.WorkspaceSummary);

        state.ActionButton = new Button
        {
            MinWidth = 138,
            Height = 26,
            Padding = new Thickness(12, 2, 12, 2),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "PrimaryJourneyActionButton"
        };
        state.ActionButton.Click += async (_, _) => await ExecuteNextActionAsync(state);
        Grid.SetColumn(state.ActionButton, 2);
        grid.Children.Add(state.ActionButton);

        return shell;
    }

    private static void Apply(State state)
    {
        var isWorkPage = !NonWorkPages.Contains(state.Main.CurrentPage);
        state.ActionBar.Visibility = isWorkPage ? Visibility.Visible : Visibility.Collapsed;
        state.ActionBar.IsHitTestVisible = isWorkPage;

        UpdateWorkspaceSummary(state);
        UpdateNextAction(state);

        if (!isWorkPage) return;

        foreach (var tag in AuxiliaryPanelTags)
        {
            var panel = FindByTag<FrameworkElement>(state.Root, tag);
            if (panel is null) continue;
            panel.Visibility = Visibility.Collapsed;
            panel.IsHitTestVisible = false;
        }

        foreach (var surface in Enumerate<FrameworkElement>(state.Root))
        {
            if (ReferenceEquals(surface, state.ActionBar)) continue;
            if (surface.Tag?.ToString() is "FloatingWorkspaceScrim" or "FloatingWorkspaceLauncher") continue;

            if (surface.Tag?.ToString() is string tag &&
                (tag.Contains("LiveOperations", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("ApprovedChanges", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("AiCopilotInbox", StringComparison.OrdinalIgnoreCase)))
            {
                surface.Visibility = Visibility.Collapsed;
                surface.IsHitTestVisible = false;
            }
        }
    }

    private static void UpdateWorkspaceSummary(State state)
    {
        var main = state.Main;
        var site = main.Sites.SelectedSite;
        if (site is null)
        {
            state.WorkspaceSummary.Text = "No website selected • Choose a website to start the guided workflow";
            return;
        }

        var pending = main.SuggestedChanges.PendingCount;
        var approved = main.SuggestedChanges.ApprovedCount;
        var ready = main.ExecutionCenter.ReadyCount;
        var running = main.DashboardRunningJobs;
        var failed = main.DashboardFailedJobs;
        var lastSync = string.IsNullOrWhiteSpace(main.DashboardLastSiteSync)
            ? "Never synchronized"
            : main.DashboardLastSiteSync;

        state.WorkspaceSummary.Text =
            $"{site.DisplayHost} • {site.StatusLabel} • Sync: {lastSync} • Pending: {pending} • Approved: {approved} • Ready: {ready} • Running: {running} • Errors: {failed}";
    }

    private static void UpdateNextAction(State state)
    {
        var next = DetermineNextAction(state.Main);
        state.NextAction = next.Action;
        state.ActionButton.Content = next.Label;
        state.ActionButton.ToolTip = next.Description;
        state.ActionButton.IsEnabled = !state.Main.IsOperationRunning && next.Action != NextAction.None;
    }

    private static NextActionInfo DetermineNextAction(MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        if (site is null)
            return new(NextAction.SelectSite, "Select website", "Open Sites and choose the website you want to manage.");

        if (!site.IsConnected)
            return new(NextAction.RetestConnection, "Retest connection", "Verify the WordPress REST API connection before continuing.");

        var hasSnapshot = main.Explorer.LoadedAt is not null || main.Explorer.LoadedItemsCount > 0;
        if (!hasSnapshot)
            return new(NextAction.Synchronize, "Synchronize", "Download the latest WordPress content into the local SQLite snapshot.");

        if (main.SuggestedChanges.Items.Count == 0)
            return new(NextAction.GenerateSuggestions, "Generate suggestions", "Analyze the synchronized snapshot and create reviewable improvements.");

        if (main.SuggestedChanges.PendingCount > 0)
            return new(NextAction.Review, "Review changes", "Review pending suggestions before approval.");

        if (main.SuggestedChanges.ApprovedCount > 0 || main.ExecutionCenter.ReadyCount > 0)
            return new(NextAction.Execute, "Execute approved", "Open Execution Center to validate, back up, execute and verify approved changes.");

        if (main.ExecutionCenter.ExecutedCount > 0)
            return new(NextAction.Verify, "Verify results", "Open Evidence Center to review execution evidence and verification results.");

        return new(NextAction.RunAudit, "Run fresh audit", "Refresh the website analysis and create the next optimization cycle.");
    }

    private static async Task ExecuteNextActionAsync(State state)
    {
        var main = state.Main;
        switch (state.NextAction)
        {
            case NextAction.SelectSite:
                await main.NavigateCommand.ExecuteAsync("Sites");
                break;

            case NextAction.RetestConnection:
                await main.NavigateCommand.ExecuteAsync("Sites");
                if (main.Sites.RetestSelectedSiteCommand.CanExecute(null))
                    await main.Sites.RetestSelectedSiteCommand.ExecuteAsync(null);
                break;

            case NextAction.Synchronize:
                await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
                if (main.Explorer.RefreshCommand.CanExecute(null))
                    await main.Explorer.RefreshCommand.ExecuteAsync(null);
                break;

            case NextAction.GenerateSuggestions:
                await main.NavigateCommand.ExecuteAsync("Suggested Changes");
                if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                    await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
                break;

            case NextAction.Review:
                await main.NavigateCommand.ExecuteAsync("Suggested Changes");
                break;

            case NextAction.Execute:
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                if (main.ExecutionCenter.LoadCommand.CanExecute(null))
                    await main.ExecutionCenter.LoadCommand.ExecuteAsync(null);
                break;

            case NextAction.Verify:
                await main.NavigateCommand.ExecuteAsync("Evidence Center");
                break;

            case NextAction.RunAudit:
                await main.NavigateCommand.ExecuteAsync("SEO Audit");
                break;
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

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed class State(Grid root, MainWindowViewModel main)
    {
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
        public Border ActionBar { get; set; } = null!;
        public TextBlock WorkspaceSummary { get; set; } = null!;
        public Button ActionButton { get; set; } = null!;
        public NextAction NextAction { get; set; }
    }

    private readonly record struct NextActionInfo(NextAction Action, string Label, string Description);

    private enum NextAction
    {
        None,
        SelectSite,
        RetestConnection,
        Synchronize,
        GenerateSuggestions,
        Review,
        Execute,
        Verify,
        RunAudit
    }
}
