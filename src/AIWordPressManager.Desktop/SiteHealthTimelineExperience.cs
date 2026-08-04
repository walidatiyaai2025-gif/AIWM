using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class SiteHealthTimelineExperience
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
        var bar = FindByTag<Border>(root, "PrimaryWorkActionBar");
        if (bar?.Child is not Grid grid) return;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Tag = "HealthTimelineActions"
        };
        Grid.SetColumn(actions, 1);
        Panel.SetZIndex(actions, 10);

        var health = Button("Health", "Open a current website health snapshot");
        health.Click += (_, _) => ShowHealthDialog(window, main);
        actions.Children.Add(health);

        var timeline = Button("Timeline", "Open the full activity timeline");
        timeline.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync("Activity Timeline");
        actions.Children.Add(timeline);

        grid.Children.Add(actions);

        window.PreviewKeyDown += async (_, args) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (args.Key == Key.H)
            {
                ShowHealthDialog(window, main);
                args.Handled = true;
            }
            else if (args.Key == Key.T)
            {
                await main.NavigateCommand.ExecuteAsync("Activity Timeline");
                args.Handled = true;
            }
        };
    }

    private static Button Button(string text, string tooltip) => new()
    {
        Content = text,
        ToolTip = tooltip,
        Margin = new Thickness(0, 0, 6, 0),
        Padding = new Thickness(10, 4, 10, 4),
        MinHeight = 26
    };

    private static void ShowHealthDialog(MainWindow owner, MainWindowViewModel main)
    {
        var score = Math.Clamp(main.DashboardHealthScore, 0, 100);
        var label = score switch
        {
            >= 90 => "Excellent",
            >= 75 => "Good",
            >= 55 => "Needs attention",
            _ => "Critical"
        };

        var report = BuildHealthReport(main, score, label);
        var dialog = new Window
        {
            Owner = owner,
            Title = "Website Health Snapshot",
            Width = 620,
            Height = 620,
            MinWidth = 520,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = Brush("SurfaceBrush", Brushes.White)
        };

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = $"{score}% • {label}",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        });
        header.Children.Add(new TextBlock
        {
            Text = main.DashboardSelectedSite,
            Margin = new Thickness(0, 4, 0, 14),
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        root.Children.Add(header);

        var text = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(14),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12
        };
        Grid.SetRow(text, 1);
        root.Children.Add(text);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var openCenter = Button("Open Health Center", "Open the detailed health page");
        openCenter.Click += async (_, _) =>
        {
            dialog.Close();
            await main.NavigateCommand.ExecuteAsync("Health Center");
        };
        buttons.Children.Add(openCenter);

        var copy = Button("Copy report", "Copy this health snapshot");
        copy.Click += (_, _) => Clipboard.SetText(report);
        buttons.Children.Add(copy);

        var close = Button("Close", "Close this snapshot");
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static string BuildHealthReport(MainWindowViewModel main, int score, string label)
    {
        var report = new StringBuilder();
        report.AppendLine("AI WORDPRESS MANAGER — WEBSITE HEALTH SNAPSHOT");
        report.AppendLine($"Generated: {DateTime.Now:g}");
        report.AppendLine($"Website: {main.DashboardSelectedSite}");
        report.AppendLine($"Overall health: {score}% ({label})");
        report.AppendLine();
        report.AppendLine($"Connection: {main.ConnectionStatus}");
        report.AppendLine($"Database: {main.DatabaseStatus}");
        report.AppendLine($"Last synchronization: {main.DashboardLastSiteSync}");
        report.AppendLine($"SEO state: {main.DashboardSeoScoreState}");
        report.AppendLine($"Technical SEO: {main.DashboardTechnicalSeoScore}%");
        report.AppendLine($"Content quality: {main.DashboardContentQualityScore}%");
        report.AppendLine($"Accessibility: {main.DashboardAccessibilityScore}%");
        report.AppendLine($"Performance: {main.DashboardPerformanceScore}%");
        report.AppendLine();
        report.AppendLine($"Open issues: {main.DashboardOpenIssues}");
        report.AppendLine($"Safe actions: {main.DashboardSafeActions}");
        report.AppendLine($"AI suggestions: {main.DashboardAiSuggestions}");
        report.AppendLine($"Execution queue: {main.DashboardQueueTotal}");
        report.AppendLine($"Running jobs: {main.DashboardRunningJobs}");
        report.AppendLine($"Failed jobs: {main.DashboardFailedJobs}");
        report.AppendLine();
        report.AppendLine("Executive summary:");
        report.AppendLine(main.DashboardExecutiveSummary);
        return report.ToString();
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindByTag<T>(child, tag);
            if (found is not null) return found;
        }
        return null;
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
