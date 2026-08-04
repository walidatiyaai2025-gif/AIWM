using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class GuidedWorkspaceExperience
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

        var panel = CreatePanel(main);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 65);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)
                or nameof(MainWindowViewModel.OperationProgress))
                Refresh();
        };

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border CreatePanel(MainWindowViewModel main)
    {
        var panel = new Border
        {
            Width = 330,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 14, 14, 14),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            Visibility = Visibility.Collapsed,
            Tag = "GuidedWorkspacePanel"
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Child = root;

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "Guided workspace",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        header.Children.Add(new TextBlock
        {
            Text = "Follow the checklist. The next valid action is always shown below.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        root.Children.Add(header);

        var checklist = new StackPanel { Tag = "JourneyChecklist" };
        checklist.Children.Add(CheckItem("Website selected", "CheckWebsite"));
        checklist.Children.Add(CheckItem("Connection verified", "CheckConnection"));
        checklist.Children.Add(CheckItem("Initial synchronization", "CheckSync"));
        checklist.Children.Add(CheckItem("AI / SEO audit", "CheckAudit"));
        checklist.Children.Add(CheckItem("Suggested changes", "CheckSuggestions"));
        checklist.Children.Add(CheckItem("Review and approval", "CheckApproval"));
        checklist.Children.Add(CheckItem("Execution", "CheckExecution"));
        checklist.Children.Add(CheckItem("Verification", "CheckVerification"));

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = checklist
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var actionCard = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = ResourceBrush("PrimaryBrush", Brushes.Teal),
            BorderThickness = new Thickness(1)
        };
        var actionStack = new StackPanel();
        actionStack.Children.Add(new TextBlock
        {
            Text = "Next action",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        actionStack.Children.Add(new TextBlock
        {
            Text = "Select a website",
            Tag = "GuidedActionTitle",
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        actionStack.Children.Add(new TextBlock
        {
            Text = "Choose a website card to begin.",
            Tag = "GuidedActionDetail",
            Margin = new Thickness(0, 5, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        var actionButton = new Button
        {
            Content = "Open Sites",
            Tag = "GuidedActionButton",
            Padding = new Thickness(12, 8, 12, 8),
            FontWeight = FontWeights.SemiBold
        };
        actionButton.Click += async (_, _) => await ExecuteNextAsync(main);
        actionStack.Children.Add(actionButton);
        actionCard.Child = actionStack;
        Grid.SetRow(actionCard, 2);
        root.Children.Add(actionCard);

        return panel;
    }

    private static Border CheckItem(string text, string tag)
    {
        var value = new TextBlock
        {
            Text = $"○  {text}",
            Tag = tag,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        };
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(9, 7, 9, 7),
            CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = value
        };
    }

    private static async Task ExecuteNextAsync(MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        if (site is null)
        {
            await main.NavigateCommand.ExecuteAsync("Sites");
            return;
        }

        var synced = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var audited = main.SeoAudit.AuditedItems > 0 || main.SeoAudit.Score > 0;
        var proposals = main.SuggestedChanges.Items.Count > 0;
        var approved = main.SuggestedChanges.ApprovedCount > 0;

        if (!site.IsConnected || !synced)
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            if (main.Explorer.RefreshCommand.CanExecute(null))
                await main.Explorer.RefreshCommand.ExecuteAsync(null);
            return;
        }

        if (!audited)
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
            return;
        }

        if (!proposals)
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
            return;
        }

        if (!approved)
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
            return;
        }

        await main.NavigateCommand.ExecuteAsync("Execution Center");
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        panel.Visibility = ShouldShow(main.CurrentPage) && main.Sites.SelectedSite is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var site = main.Sites.SelectedSite;
        var selected = site is not null;
        var connected = site?.IsConnected == true;
        var synced = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var audited = main.SeoAudit.AuditedItems > 0 || main.SeoAudit.Score > 0;
        var proposals = main.SuggestedChanges.Items.Count > 0;
        var approved = main.SuggestedChanges.ApprovedCount > 0;
        var executed = main.DashboardCompletedJobs > 0 && approved;
        var verified = main.JourneyVerifyState.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                       || main.JourneyDoneState.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);

        SetCheck(panel, "CheckWebsite", "Website selected", selected, !selected);
        SetCheck(panel, "CheckConnection", "Connection verified", connected, selected && !connected);
        SetCheck(panel, "CheckSync", "Initial synchronization", synced, connected && !synced);
        SetCheck(panel, "CheckAudit", "AI / SEO audit", audited, synced && !audited);
        SetCheck(panel, "CheckSuggestions", "Suggested changes", proposals, audited && !proposals);
        SetCheck(panel, "CheckApproval", "Review and approval", approved, proposals && !approved);
        SetCheck(panel, "CheckExecution", "Execution", executed, approved && !executed);
        SetCheck(panel, "CheckVerification", "Verification", verified, executed && !verified);

        string title;
        string detail;
        string button;

        if (!connected || !synced)
        {
            title = "Synchronize the website";
            detail = "Connection and local WordPress data are required before analysis.";
            button = "Start synchronization";
        }
        else if (!audited)
        {
            title = "Run AI / SEO audit";
            detail = "Create a measurable baseline before generating changes.";
            button = "Start audit";
        }
        else if (!proposals)
        {
            title = "Generate suggested changes";
            detail = "Convert audit findings into safe, explainable proposals.";
            button = "Generate changes";
        }
        else if (!approved)
        {
            title = "Review and approve";
            detail = $"{main.SuggestedChanges.PendingCount} proposal(s) are waiting for review.";
            button = "Review proposals";
        }
        else
        {
            title = "Execute approved changes";
            detail = $"{main.SuggestedChanges.ApprovedCount} approved change(s) are ready for controlled execution.";
            button = "Open execution center";
        }

        SetText(panel, "GuidedActionTitle", title);
        SetText(panel, "GuidedActionDetail", detail);
        var action = Find<Button>(panel, "GuidedActionButton");
        if (action is not null)
        {
            action.Content = button;
            action.IsEnabled = !main.IsOperationRunning;
        }
    }

    private static bool ShouldShow(string page) => page is not "Sites" and not "Dashboard" and not "Notification Center";

    private static void SetCheck(DependencyObject root, string tag, string title, bool complete, bool current)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is null) return;
        text.Text = complete ? $"✓  {title}" : current ? $"▶  {title}" : $"○  {title}";
        text.FontWeight = current ? FontWeights.Bold : FontWeights.Normal;
        text.Foreground = complete
            ? Brushes.SeaGreen
            : current
                ? ResourceBrush("PrimaryBrush", Brushes.Teal)
                : ResourceBrush("TextSecondaryBrush", Brushes.DimGray);
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
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
