using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class MissionControlExperience
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
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        Attached.Add(window, new object());
        var view = CreateView(main);
        Grid.SetRow(view, 3);
        Panel.SetZIndex(view, 60);
        root.Children.Add(view);

        void Refresh() => RefreshView(view, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)
                or nameof(MainWindowViewModel.OperationProgress)
                or nameof(MainWindowViewModel.DashboardHealthScore)
                or nameof(MainWindowViewModel.DashboardRunningJobs)
                or nameof(MainWindowViewModel.DashboardFailedJobs))
                Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border CreateView(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(14),
            Background = ResourceBrush("WindowBackgroundBrush", Brushes.WhiteSmoke),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "MissionControlRoot"
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Child = layout;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Mission Control",
                    FontSize = 25,
                    FontWeight = FontWeights.Bold,
                    Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
                },
                new TextBlock
                {
                    Text = "See what needs attention, what is running, and the next best action.",
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
                }
            }
        });

        var headerActions = new StackPanel { Orientation = Orientation.Horizontal };
        headerActions.Children.Add(ActionButton("Sites", async () => await main.NavigateCommand.ExecuteAsync("Sites")));
        headerActions.Children.Add(ActionButton("Operations", async () => await main.NavigateCommand.ExecuteAsync("Operations Center")));
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        layout.Children.Add(header);

        var metrics = new UniformGrid { Rows = 1, Columns = 5, Margin = new Thickness(0, 0, 0, 14) };
        metrics.Children.Add(MetricCard("Workspace", "No site selected", "MetricWorkspace"));
        metrics.Children.Add(MetricCard("Project Health", "0/100", "MetricHealth"));
        metrics.Children.Add(MetricCard("Running Jobs", "0", "MetricRunning"));
        metrics.Children.Add(MetricCard("Pending Review", "0", "MetricPending"));
        metrics.Children.Add(MetricCard("Ready to Execute", "0", "MetricApproved"));
        Grid.SetRow(metrics, 1);
        layout.Children.Add(metrics);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 2);
        layout.Children.Add(body);

        var recommendation = PanelCard("Recommended next action");
        var recommendationStack = (StackPanel)recommendation.Child;
        recommendationStack.Children.Add(new TextBlock
        {
            Text = "Select a website",
            Tag = "MissionActionTitle",
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        recommendationStack.Children.Add(new TextBlock
        {
            Text = "Choose a website card to start the guided workflow.",
            Tag = "MissionActionDescription",
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        recommendationStack.Children.Add(new TextBlock
        {
            Text = "Estimated time: less than 1 minute",
            Tag = "MissionActionMeta",
            Margin = new Thickness(0, 8, 0, 14),
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        var nextButton = ActionButton("Open Sites", async () => await ExecuteNextActionAsync(main));
        nextButton.Tag = "MissionActionButton";
        recommendationStack.Children.Add(nextButton);
        body.Children.Add(recommendation);

        var attention = PanelCard("Live status");
        var attentionStack = (StackPanel)attention.Child;
        attentionStack.Children.Add(StatusLine("Current operation", "Idle", "MissionOperation"));
        attentionStack.Children.Add(StatusLine("Progress", "0%", "MissionProgress"));
        attentionStack.Children.Add(StatusLine("Failed jobs", "0", "MissionFailed"));
        attentionStack.Children.Add(StatusLine("Last sync", "Never synchronized", "MissionLastSync"));
        Grid.SetColumn(attention, 1);
        attention.Margin = new Thickness(12, 0, 0, 0);
        body.Children.Add(attention);

        return shell;
    }

    private static Border MetricCard(string title, string value, string tag)
    {
        var card = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1)
        };
        card.Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray) },
                new TextBlock
                {
                    Text = value,
                    Tag = tag,
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
                }
            }
        };
        return card;
    }

    private static Border PanelCard(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private static Grid StatusLine(string title, string value, string tag)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = title, Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray) });
        var valueText = new TextBlock
        {
            Text = value,
            Tag = tag,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static async Task ExecuteNextActionAsync(MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        if (site is null)
        {
            await main.NavigateCommand.ExecuteAsync("Sites");
            return;
        }

        var synced = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        if (!site.IsConnected || !synced)
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            if (main.Explorer.SynchronizeNowCommand.CanExecute(null))
                await main.Explorer.SynchronizeNowCommand.ExecuteAsync(null);
            return;
        }

        var auditExists = main.SeoAudit.AuditedItems > 0 || main.SeoAudit.Score > 0;
        if (!auditExists)
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
            return;
        }

        if (main.SuggestedChanges.Items.Count == 0)
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
            return;
        }

        if (main.SuggestedChanges.ApprovedCount > 0)
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            return;
        }

        await main.NavigateCommand.ExecuteAsync("Suggested Changes");
        await main.SuggestedChanges.LoadAsync();
    }

    private static void RefreshView(Border root, MainWindowViewModel main)
    {
        root.Visibility = main.CurrentPage == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        if (root.Visibility != Visibility.Visible) return;

        var site = main.Sites.SelectedSite;
        SetText(root, "MetricWorkspace", site?.Name ?? "No site selected");
        SetText(root, "MetricHealth", $"{Math.Clamp(main.DashboardHealthScore, 0, 100)}/100");
        SetText(root, "MetricRunning", Math.Max(main.DashboardRunningJobs, main.IsOperationRunning ? 1 : 0).ToString());
        SetText(root, "MetricPending", main.SuggestedChanges.PendingCount.ToString());
        SetText(root, "MetricApproved", main.SuggestedChanges.ApprovedCount.ToString());
        SetText(root, "MissionOperation", main.IsOperationRunning ? main.OperationTitle : "Idle");
        SetText(root, "MissionProgress", $"{Math.Clamp(main.OperationProgress, 0, 100)}%");
        SetText(root, "MissionFailed", main.DashboardFailedJobs.ToString());
        SetText(root, "MissionLastSync", main.DashboardLastSiteSync);

        var synced = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var auditExists = main.SeoAudit.AuditedItems > 0 || main.SeoAudit.Score > 0;
        string title;
        string description;
        string meta;
        string button;

        if (site is null)
        {
            title = "Select a website";
            description = "Choose a website card to make it the current workspace.";
            meta = "Estimated time: less than 1 minute";
            button = "Open Sites";
        }
        else if (!site.IsConnected || !synced)
        {
            title = "Run initial synchronization";
            description = "Read posts, pages, media, categories, tags, theme, and WordPress metadata.";
            meta = "Expected result: a complete local workspace";
            button = "Start synchronization";
        }
        else if (!auditExists)
        {
            title = "Run AI / SEO audit";
            description = "Establish the measurable baseline before approving any WordPress changes.";
            meta = "Estimated time: about 2 minutes";
            button = "Start audit";
        }
        else if (main.SuggestedChanges.Items.Count == 0)
        {
            title = "Generate actionable proposals";
            description = "Convert audit findings into explainable, risk-scored changes for review.";
            meta = $"SEO score: {main.SeoAudit.Score}%";
            button = "Generate changes";
        }
        else if (main.SuggestedChanges.ApprovedCount > 0)
        {
            title = "Execute approved changes";
            description = "Review backup, verification, and evidence settings before writing to WordPress.";
            meta = $"Ready to execute: {main.SuggestedChanges.ApprovedCount}";
            button = "Open execution center";
        }
        else
        {
            title = "Review and approve proposals";
            description = "Compare before and after values, risk, impact, and execution plans.";
            meta = $"Pending review: {main.SuggestedChanges.PendingCount}";
            button = "Review proposals";
        }

        SetText(root, "MissionActionTitle", title);
        SetText(root, "MissionActionDescription", description);
        SetText(root, "MissionActionMeta", meta);
        var action = Find<Button>(root, "MissionActionButton");
        if (action is not null)
        {
            action.Content = button;
            action.IsEnabled = !main.IsOperationRunning;
        }
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
    }

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(12, 7, 12, 7)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static T? Find<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && Equals(element.Tag, tag)) return element;
            var nested = Find<T>(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
