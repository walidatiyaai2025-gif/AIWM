using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class LiveOperationsExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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

        var controls = BuildPanel(main);
        var state = new State(window, main, root, controls);
        Attached.Add(window, state);

        Grid.SetRow(controls.Panel, 3);
        Panel.SetZIndex(controls.Panel, 1000);
        root.Children.Add(controls.Panel);

        main.PropertyChanged += state.OnMainPropertyChanged;
        window.Activated += state.OnWindowStateChanged;
        window.Deactivated += state.OnWindowStateChanged;
        window.StateChanged += state.OnWindowStateChanged;
        window.Closed += state.OnClosed;

        ApplyVisibility(state);
        Refresh(state);
    }

    private static PanelControls BuildPanel(MainWindowViewModel main)
    {
        var panel = new Border
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
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Tag = "LiveOperationsPanel"
        };

        var stack = new StackPanel();
        panel.Child = stack;

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

        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        stack.Children.Add(body);

        var operationState = new TextBlock
        {
            Text = "Idle",
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        };
        body.Children.Add(operationState);

        var operationTitle = new TextBlock
        {
            Text = "No background operation is running.",
            Margin = new Thickness(0, 5, 0, 0),
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(operationTitle);

        var operationStep = new TextBlock
        {
            Text = "Waiting for the next task.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray),
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(operationStep);

        var operationProgress = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 10, 0, 0),
            Value = 0
        };
        body.Children.Add(operationProgress);

        var operationPercent = new TextBlock
        {
            Text = "0%",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        };
        body.Children.Add(operationPercent);

        var operationDetail = new TextBlock
        {
            Text = string.Empty,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 90,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        };
        body.Children.Add(operationDetail);

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

        return new PanelControls(panel, operationState, operationTitle, operationStep,
            operationDetail, operationProgress, operationPercent);
    }

    private static void ApplyVisibility(State state)
    {
        var pageAllowsPanel = state.Main.CurrentPage.Equals("Jobs", StringComparison.OrdinalIgnoreCase) ||
                              state.Main.CurrentPage.Equals("Operations Center", StringComparison.OrdinalIgnoreCase);
        var visible = pageAllowsPanel && state.Window.IsActive && state.Window.WindowState != WindowState.Minimized;
        state.Controls.Panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        state.Controls.Panel.IsHitTestVisible = visible;
        if (visible) Refresh(state);
    }

    private static void Refresh(State state)
    {
        var controls = state.Controls;
        if (controls.Panel.Visibility != Visibility.Visible) return;

        var main = state.Main;
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

        controls.State.Text = running ? "● RUNNING" : value >= 100 ? "✓ COMPLETED" : "○ IDLE";
        controls.State.Foreground = running
            ? ResourceBrush("PrimaryBrush", Brushes.Teal)
            : value >= 100 ? Brushes.ForestGreen : ResourceBrush("TextSecondaryBrush", Brushes.DimGray);
        controls.Title.Text = string.IsNullOrWhiteSpace(currentTitle) ? "Ready" : currentTitle;
        controls.Step.Text = string.IsNullOrWhiteSpace(currentStep) ? "Idle" : currentStep;
        controls.Detail.Text = string.IsNullOrWhiteSpace(currentDetail)
            ? "No background operation is running."
            : currentDetail;
        controls.Progress.Value = value;
        controls.Percent.Text = $"{value}%";
        controls.Panel.Opacity = running ? 1 : 0.94;
    }

    private static bool IsOperationProperty(string? propertyName) => propertyName is
        nameof(MainWindowViewModel.IsOperationRunning) or
        nameof(MainWindowViewModel.OperationProgress) or
        nameof(MainWindowViewModel.OperationTitle) or
        nameof(MainWindowViewModel.OperationStep) or
        nameof(MainWindowViewModel.OperationDetail) or
        nameof(MainWindowViewModel.IsGuidedAnalysisRunning) or
        nameof(MainWindowViewModel.GuidedAnalysisProgress) or
        nameof(MainWindowViewModel.GuidedAnalysisStage) or
        nameof(MainWindowViewModel.GuidedAnalysisDetail) or
        nameof(MainWindowViewModel.IsSafeAutopilotRunning) or
        nameof(MainWindowViewModel.SafeAutopilotProgress) or
        nameof(MainWindowViewModel.SafeAutopilotStage) or
        nameof(MainWindowViewModel.SafeAutopilotSummary);

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record PanelControls(
        Border Panel,
        TextBlock State,
        TextBlock Title,
        TextBlock Step,
        TextBlock Detail,
        ProgressBar Progress,
        TextBlock Percent);

    private sealed class State(MainWindow window, MainWindowViewModel main, Grid root, PanelControls controls)
    {
        public MainWindow Window { get; } = window;
        public MainWindowViewModel Main { get; } = main;
        public Grid Root { get; } = root;
        public PanelControls Controls { get; } = controls;

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                ApplyVisibility(this);
                return;
            }

            if (IsOperationProperty(e.PropertyName))
                Refresh(this);
        }

        public void OnWindowStateChanged(object? sender, EventArgs e) => ApplyVisibility(this);

        public void OnClosed(object? sender, EventArgs e)
        {
            Main.PropertyChanged -= OnMainPropertyChanged;
            Window.Activated -= OnWindowStateChanged;
            Window.Deactivated -= OnWindowStateChanged;
            Window.StateChanged -= OnWindowStateChanged;
            Window.Closed -= OnClosed;

            Root.Children.Remove(Controls.Panel);
            Controls.Panel.Child = null;
        }
    }
}
