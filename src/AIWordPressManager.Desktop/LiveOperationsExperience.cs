using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class LiveOperationsExperience
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
        if (root.RowDefinitions.Count < 4) return;

        Attached.Add(window, new object());

        var panel = BuildPanel(main);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 1000);
        root.Children.Add(panel);

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => Refresh(panel, main);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh(panel, main);
    }

    private static Border BuildPanel(MainWindowViewModel main)
    {
        var border = new Border
        {
            Width = 330,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 14, 0),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            Tag = "LiveOperationsPanel"
        };

        var stack = new StackPanel();
        border.Child = stack;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stack.Children.Add(header);

        header.Children.Add(new TextBlock
        {
            Text = "Live operations",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });

        var toggle = new Button
        {
            Content = "−",
            Width = 28,
            Height = 24,
            Padding = new Thickness(0),
            ToolTip = "Collapse or expand live operations"
        };
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);

        var body = new StackPanel { Tag = "LiveOperationsBody", Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(body);

        body.Children.Add(new TextBlock
        {
            Text = "Idle",
            Tag = "OperationState",
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        body.Children.Add(new TextBlock
        {
            Text = "No background operation is running.",
            Tag = "OperationTitle",
            Margin = new Thickness(0, 5, 0, 0),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = "Waiting for the next task.",
            Tag = "OperationStep",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray),
            TextWrapping = TextWrapping.Wrap
        });

        body.Children.Add(new ProgressBar
        {
            Tag = "OperationProgress",
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 10, 0, 0),
            Value = 0
        });
        body.Children.Add(new TextBlock
        {
            Text = "0%",
            Tag = "OperationPercent",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        body.Children.Add(new TextBlock
        {
            Text = string.Empty,
            Tag = "OperationDetail",
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 90,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(actions);
        var jobsButton = new Button { Content = "Open Jobs", Padding = new Thickness(10, 5, 10, 5) };
        jobsButton.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync("Jobs");
        actions.Children.Add(jobsButton);
        var operationsButton = new Button
        {
            Content = "Operations Center",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5)
        };
        operationsButton.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync("Operations Center");
        actions.Children.Add(operationsButton);

        toggle.Click += (_, _) =>
        {
            body.Visibility = body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            toggle.Content = body.Visibility == Visibility.Visible ? "−" : "+";
        };

        return border;
    }

    private static void Refresh(Border panel, MainWindowViewModel main)
    {
        var state = Find<TextBlock>(panel, "OperationState");
        var title = Find<TextBlock>(panel, "OperationTitle");
        var step = Find<TextBlock>(panel, "OperationStep");
        var detail = Find<TextBlock>(panel, "OperationDetail");
        var progress = Find<ProgressBar>(panel, "OperationProgress");
        var percent = Find<TextBlock>(panel, "OperationPercent");

        var running = main.IsOperationRunning || main.IsGuidedAnalysisRunning || main.IsSafeAutopilotRunning;
        var value = main.IsSafeAutopilotRunning
            ? main.SafeAutopilotProgress
            : main.IsGuidedAnalysisRunning
                ? main.GuidedAnalysisProgress
                : main.OperationProgress;
        value = Math.Clamp(value, 0, 100);

        var currentTitle = main.IsSafeAutopilotRunning
            ? "Safe Autopilot"
            : main.IsGuidedAnalysisRunning
                ? "Guided analysis"
                : main.OperationTitle;
        var currentStep = main.IsSafeAutopilotRunning
            ? main.SafeAutopilotStage
            : main.IsGuidedAnalysisRunning
                ? main.GuidedAnalysisStage
                : main.OperationStep;
        var currentDetail = main.IsSafeAutopilotRunning
            ? main.SafeAutopilotSummary
            : main.IsGuidedAnalysisRunning
                ? main.GuidedAnalysisDetail
                : main.OperationDetail;

        if (state is not null)
        {
            state.Text = running ? "● RUNNING" : value >= 100 ? "✓ COMPLETED" : "○ IDLE";
            state.Foreground = running
                ? ResourceBrush("PrimaryBrush", Brushes.Teal)
                : value >= 100 ? Brushes.ForestGreen : ResourceBrush("TextSecondaryBrush", Brushes.DimGray);
        }
        if (title is not null) title.Text = string.IsNullOrWhiteSpace(currentTitle) ? "Ready" : currentTitle;
        if (step is not null) step.Text = string.IsNullOrWhiteSpace(currentStep) ? "Idle" : currentStep;
        if (detail is not null) detail.Text = string.IsNullOrWhiteSpace(currentDetail) ? "No background operation is running." : currentDetail;
        if (progress is not null) progress.Value = value;
        if (percent is not null) percent.Text = $"{value}%";

        panel.Opacity = running ? 1 : 0.94;
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
