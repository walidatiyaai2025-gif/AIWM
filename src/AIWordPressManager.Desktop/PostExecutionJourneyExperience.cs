using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class PostExecutionJourneyExperience
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
        Panel.SetZIndex(card, 95);
        root.Children.Add(card);

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => Refresh(card, main);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh(card, main);
    }

    private static Border CreateCard(MainWindowViewModel main)
    {
        var card = new Border
        {
            Width = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("PrimaryBrush", Brushes.Teal),
            Visibility = Visibility.Collapsed
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.Child = grid;

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = "Execution verified",
            Tag = "PostExecutionTitle",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        text.Children.Add(new TextBlock
        {
            Text = "The selected WordPress changes were executed and verified.",
            Tag = "PostExecutionSummary",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        text.Children.Add(new TextBlock
        {
            Text = "Recommended next step: rerun the audit and measure the improvement.",
            Tag = "PostExecutionMeta",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        grid.Children.Add(text);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var audit = new Button
        {
            Content = "Rerun audit",
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold
        };
        audit.Click += async (_, _) =>
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
        };
        actions.Children.Add(audit);

        var mission = new Button
        {
            Content = "Mission Control",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 8, 14, 8)
        };
        mission.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync("Dashboard");
        actions.Children.Add(mission);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return card;
    }

    private static void Refresh(Border card, MainWindowViewModel main)
    {
        var execution = main.ExecutionCenter;
        var visible = main.CurrentPage == "Execution Center" && !execution.IsBusy && execution.ExecutedCount > 0;
        card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        var title = Find<TextBlock>(card, "PostExecutionTitle");
        var summary = Find<TextBlock>(card, "PostExecutionSummary");
        var meta = Find<TextBlock>(card, "PostExecutionMeta");

        if (title is not null)
            title.Text = execution.FailedCount == 0
                ? "Execution verified"
                : "Execution completed with attention required";

        if (summary is not null)
            summary.Text = $"Verified: {execution.ExecutedCount} • Failed: {execution.FailedCount} • Ready remaining: {execution.ReadyCount}.";

        if (meta is not null)
            meta.Text = execution.FailedCount == 0
                ? "Recommended next step: rerun the AI / SEO audit and compare the new score."
                : "Retry failed items or use rollback before starting a new audit.";
    }

    private static T? Find<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && Equals(element.Tag, tag)) return element;
            var nested = Find<T>(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
