using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class SmartNavigationExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> EnhancedSiteCards = new();

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
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var state = new State(window, root, main);
        Attached.Add(window, state);

        state.EmptyState = BuildEmptyState(state);
        Grid.SetRow(state.EmptyState, 3);
        Panel.SetZIndex(state.EmptyState, 40);
        root.Children.Add(state.EmptyState);

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(550)
        };
        timer.Tick += (_, _) => Refresh(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh(state);
    }

    private static Border BuildEmptyState(State state)
    {
        var shell = new Border
        {
            Width = 520,
            MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(32),
            CornerRadius = new CornerRadius(16),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "ProfessionalEmptyState"
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "◎",
            FontSize = 42,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "EmptyStateTitle",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 6),
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "EmptyStateDescription",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        var action = new Button
        {
            Tag = "EmptyStateAction",
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 190,
            Margin = new Thickness(0, 20, 0, 0),
            Padding = new Thickness(18, 9, 18, 9)
        };
        action.Click += async (_, _) => await ExecuteEmptyStateActionAsync(state);
        stack.Children.Add(action);
        return shell;
    }

    private static void Refresh(State state)
    {
        EnhanceSiteCards(state);
        RefreshBreadcrumbAndContextActions(state);
        RefreshEmptyState(state);
    }

    private static void EnhanceSiteCards(State state)
    {
        foreach (var element in Enumerate<FrameworkElement>(state.Root))
        {
            if (element.DataContext is not SiteCardViewModel site) continue;
            if (element is not Button && element is not Border) continue;
            if (EnhancedSiteCards.TryGetValue(element, out _)) continue;

            EnhancedSiteCards.Add(element, new object());
            element.Cursor = Cursors.Hand;
            element.ToolTip = "Open this website workspace. Right-click for available actions.";
            element.ContextMenu = BuildSiteContextMenu(state, site);

            if (element is Button button)
            {
                button.Click += async (_, _) => await OpenSiteWorkspaceAsync(state, site, "Dashboard");
            }
            else
            {
                element.MouseLeftButtonUp += async (_, args) =>
                {
                    if (args.OriginalSource is Button) return;
                    args.Handled = true;
                    await OpenSiteWorkspaceAsync(state, site, "Dashboard");
                };
            }
        }
    }

    private static ContextMenu BuildSiteContextMenu(State state, SiteCardViewModel site)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("Open dashboard", async () => await OpenSiteWorkspaceAsync(state, site, "Dashboard")));
        menu.Items.Add(Menu("Open WordPress Explorer", async () => await OpenSiteWorkspaceAsync(state, site, "WordPress Explorer")));
        menu.Items.Add(Menu("Run SEO audit", async () => await OpenSiteWorkspaceAsync(state, site, "SEO Audit")));
        menu.Items.Add(Menu("Review suggested changes", async () => await OpenSiteWorkspaceAsync(state, site, "Suggested Changes")));
        menu.Items.Add(Menu("Open execution center", async () => await OpenSiteWorkspaceAsync(state, site, "Execution Center")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("Retest connection", async () =>
        {
            await state.Main.Sites.SelectSiteCommand.ExecuteAsync(site);
            if (state.Main.Sites.RetestSelectedSiteCommand.CanExecute(null))
                await state.Main.Sites.RetestSelectedSiteCommand.ExecuteAsync(null);
        }));
        menu.Items.Add(Menu("Open WordPress admin", async () =>
        {
            await state.Main.Sites.SelectSiteCommand.ExecuteAsync(site);
            state.Main.Sites.OpenWordPressAdminCommand.Execute(null);
        }));
        menu.Items.Add(Menu("Copy website URL", async () =>
        {
            await state.Main.Sites.SelectSiteCommand.ExecuteAsync(site);
            state.Main.Sites.CopySelectedUrlCommand.Execute(null);
        }));
        return menu;
    }

    private static MenuItem Menu(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header, Padding = new Thickness(12, 7, 18, 7) };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static async Task OpenSiteWorkspaceAsync(State state, SiteCardViewModel site, string destination)
    {
        await state.Main.Sites.SelectSiteCommand.ExecuteAsync(site);
        await state.Main.NavigateCommand.ExecuteAsync(destination);
    }

    private static void RefreshBreadcrumbAndContextActions(State state)
    {
        var actionBar = FindByTag<Border>(state.Root, "PrimaryWorkActionBar");
        if (actionBar?.Child is not Grid grid) return;

        var breadcrumb = FindByTag<TextBlock>(actionBar, "SmartBreadcrumb");
        if (breadcrumb is null)
        {
            breadcrumb = new TextBlock
            {
                Tag = "SmartBreadcrumb",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Foreground = Brush("TextSecondaryBrush", Brushes.DimGray),
                Margin = new Thickness(12, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(breadcrumb, 1);
            grid.Children.Add(breadcrumb);
        }

        var selected = state.Main.Sites.SelectedSite;
        breadcrumb.Text = selected is null
            ? $"Sites  ›  {state.Main.CurrentPage}"
            : $"Sites  ›  {selected.DisplayHost}  ›  {state.Main.CurrentPage}";

        var contextual = FindByTag<WrapPanel>(actionBar, "ContextualPageActions");
        if (contextual is null)
        {
            contextual = new WrapPanel
            {
                Tag = "ContextualPageActions",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 144, 0)
            };
            Grid.SetColumn(contextual, 2);
            grid.Children.Add(contextual);
        }

        var pageKey = state.Main.CurrentPage;
        if (Equals(contextual.DataContext, pageKey)) return;
        contextual.DataContext = pageKey;
        contextual.Children.Clear();

        AddContextualActions(contextual, state, pageKey);
    }

    private static void AddContextualActions(WrapPanel panel, State state, string page)
    {
        panel.Children.Add(ContextButton("Refresh", async () =>
        {
            if (state.Main.RefreshCurrentPageCommand.CanExecute(null))
                await state.Main.RefreshCurrentPageCommand.ExecuteAsync(null);
        }));

        switch (page)
        {
            case "SEO Audit":
            case "Content Audit":
                panel.Children.Add(ContextButton("Review changes", async () => await state.Main.NavigateCommand.ExecuteAsync("Suggested Changes")));
                panel.Children.Add(ContextButton("Execute", async () => await state.Main.NavigateCommand.ExecuteAsync("Execution Center")));
                break;
            case "Suggested Changes":
            case "Approval Queue":
                panel.Children.Add(ContextButton("Approval", async () => await state.Main.NavigateCommand.ExecuteAsync("Approval Queue")));
                panel.Children.Add(ContextButton("Execution", async () => await state.Main.NavigateCommand.ExecuteAsync("Execution Center")));
                break;
            case "Execution Center":
                panel.Children.Add(ContextButton("Jobs", async () => await state.Main.NavigateCommand.ExecuteAsync("Jobs")));
                panel.Children.Add(ContextButton("Evidence", async () => await state.Main.NavigateCommand.ExecuteAsync("Evidence Center")));
                break;
            case "WordPress Explorer":
                panel.Children.Add(ContextButton("SEO Audit", async () => await state.Main.NavigateCommand.ExecuteAsync("SEO Audit")));
                panel.Children.Add(ContextButton("Media review", async () => await state.Main.NavigateCommand.ExecuteAsync("AI Studio")));
                break;
        }
    }

    private static Button ContextButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(9, 4, 9, 4),
            MinHeight = 24
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void RefreshEmptyState(State state)
    {
        var sites = state.Main.Sites;
        var show = false;
        var title = string.Empty;
        var description = string.Empty;
        var action = string.Empty;
        state.EmptyAction = EmptyAction.None;

        if (state.Main.CurrentPage == "Sites" && !sites.HasSites && !sites.IsLoading)
        {
            show = true;
            title = "No websites yet";
            description = "Add your first authorized WordPress website to start synchronization, analysis and safe execution.";
            action = "Add your first website";
            state.EmptyAction = EmptyAction.AddSite;
        }
        else if (state.Main.CurrentPage == "Sites" && sites.HasFilteredEmptyState && !sites.IsLoading)
        {
            show = true;
            title = "No websites match these filters";
            description = "Clear the current search and status filters to show all registered websites.";
            action = "Clear filters";
            state.EmptyAction = EmptyAction.ClearSiteFilters;
        }
        else if (state.Main.CurrentPage == "Suggested Changes" && state.Main.SuggestedChanges.Items.Count == 0)
        {
            show = true;
            title = "No suggestions are waiting";
            description = "Run a fresh audit to analyze the synchronized website and prepare reviewable improvements.";
            action = "Run SEO audit";
            state.EmptyAction = EmptyAction.RunAudit;
        }
        else if (state.Main.CurrentPage == "Execution Center" && state.Main.ExecutionCenter.Items.Count == 0)
        {
            show = true;
            title = "No execution jobs";
            description = "Approve suggested changes first. Supported actions will then appear here for safe execution and verification.";
            action = "Review suggested changes";
            state.EmptyAction = EmptyAction.ReviewSuggestions;
        }

        state.EmptyState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        state.EmptyState.IsHitTestVisible = show;
        if (!show) return;
        SetText(state.EmptyState, "EmptyStateTitle", title);
        SetText(state.EmptyState, "EmptyStateDescription", description);
        var button = FindByTag<Button>(state.EmptyState, "EmptyStateAction");
        if (button is not null) button.Content = action;
    }

    private static async Task ExecuteEmptyStateActionAsync(State state)
    {
        switch (state.EmptyAction)
        {
            case EmptyAction.AddSite:
                state.Main.Sites.AddSiteCommand.Execute(null);
                break;
            case EmptyAction.ClearSiteFilters:
                state.Main.Sites.ClearFiltersCommand.Execute(null);
                break;
            case EmptyAction.RunAudit:
                await state.Main.NavigateCommand.ExecuteAsync("SEO Audit");
                break;
            case EmptyAction.ReviewSuggestions:
                await state.Main.NavigateCommand.ExecuteAsync("Suggested Changes");
                break;
        }
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = FindByTag<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
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

    private sealed class State(MainWindow window, Grid root, MainWindowViewModel main)
    {
        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
        public Border EmptyState { get; set; } = null!;
        public EmptyAction EmptyAction { get; set; }
    }

    private enum EmptyAction
    {
        None,
        AddSite,
        ClearSiteFilters,
        RunAudit,
        ReviewSuggestions
    }
}
