using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class WebsiteReadyJourneyExperience
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
        var card = CreateCard(main);
        Grid.SetRow(card, 3);
        Panel.SetZIndex(card, 75);
        root.Children.Add(card);

        void Refresh() => RefreshCard(card, main);
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

    private static Border CreateCard(MainWindowViewModel main)
    {
        var card = new Border
        {
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(18, 14, 18, 14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("PrimaryBrush", Brushes.Teal),
            Visibility = Visibility.Collapsed,
            Tag = "WebsiteReadyCard"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = "Website ready",
            Tag = "ReadyTitle",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        text.Children.Add(new TextBlock
        {
            Text = "The first synchronization is complete.",
            Tag = "ReadySummary",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        text.Children.Add(new TextBlock
        {
            Text = "Recommended next step • AI / SEO audit • Estimated time: 2 minutes",
            Tag = "ReadyMeta",
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 11,
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        grid.Children.Add(text);

        var button = new Button
        {
            Content = "Start AI audit",
            Tag = "ReadyAction",
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(16, 9, 16, 9),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        button.Click += async (_, _) =>
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return card;
    }

    private static void RefreshCard(Border card, MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        var connected = site?.IsConnected == true;
        var synchronized = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var auditRunning = main.SeoAudit.IsRunning;
        var auditExists = main.SeoAudit.AuditedItems > 0 || main.SeoAudit.Score > 0;

        card.Visibility = connected && synchronized ? Visibility.Visible : Visibility.Collapsed;
        if (card.Visibility != Visibility.Visible) return;

        var title = Find<TextBlock>(card, "ReadyTitle");
        var summary = Find<TextBlock>(card, "ReadySummary");
        var meta = Find<TextBlock>(card, "ReadyMeta");
        var action = Find<Button>(card, "ReadyAction");

        if (auditRunning)
        {
            if (title is not null) title.Text = "Analyzing website…";
            if (summary is not null) summary.Text = main.SeoAudit.StatusMessage;
            if (meta is not null) meta.Text = "AI Analysis is the current journey step";
            if (action is not null)
            {
                action.Content = "Audit running";
                action.IsEnabled = false;
            }
            return;
        }

        if (auditExists)
        {
            if (title is not null) title.Text = $"Audit ready • Score {main.SeoAudit.Score}%";
            if (summary is not null) summary.Text = $"Found {main.SeoAudit.HighIssues + main.SeoAudit.MediumIssues + main.SeoAudit.LowIssues} measurable issue(s). Review the findings and prepare changes.";
            if (meta is not null) meta.Text = $"High: {main.SeoAudit.HighIssues} • Medium: {main.SeoAudit.MediumIssues} • Low: {main.SeoAudit.LowIssues}";
            if (action is not null)
            {
                action.Content = "Review findings";
                action.IsEnabled = true;
            }
            return;
        }

        if (title is not null) title.Text = $"{site!.Name} is ready";
        if (summary is not null) summary.Text = "The first WordPress synchronization is complete. Establish the SEO baseline before approving or executing changes.";
        if (meta is not null) meta.Text = "Recommended next step • AI / SEO audit • Estimated time: 2 minutes";
        if (action is not null)
        {
            action.Content = "Start AI audit";
            action.IsEnabled = true;
        }
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
