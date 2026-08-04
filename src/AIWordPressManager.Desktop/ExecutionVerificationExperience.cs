using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ExecutionVerificationExperience
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
        var console = CreateConsole(main);
        Grid.SetRow(console, 3);
        Panel.SetZIndex(console, 68);
        root.Children.Add(console);

        void Refresh() => RefreshConsole(console, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)
                or nameof(MainWindowViewModel.OperationProgress))
                Refresh();
        };
        main.ExecutionCenter.PropertyChanged += (_, _) => Refresh();
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border CreateConsole(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Width = 430,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 12, 18, 12),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("PrimaryBrush", Brushes.Teal),
            Visibility = Visibility.Collapsed,
            Tag = "ExecutionVerificationConsole"
        };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var stack = new StackPanel();
        scroll.Content = stack;
        shell.Child = scroll;

        stack.Children.Add(new TextBlock
        {
            Text = "Execution & Verification",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Plan, execute, verify, retry, and roll back approved WordPress changes.",
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        metrics.Children.Add(Metric("Ready", "0", "ExecutionReady"));
        metrics.Children.Add(Metric("Executed", "0", "ExecutionExecuted"));
        metrics.Children.Add(Metric("Failed", "0", "ExecutionFailed"));
        metrics.Children.Add(Metric("Blocked", "0", "ExecutionBlocked"));
        metrics.Children.Add(Metric("Selected", "0", "ExecutionSelected"));
        metrics.Children.Add(Metric("Progress", "0%", "ExecutionProgress"));
        stack.Children.Add(metrics);

        stack.Children.Add(SectionTitle("Execution plan"));
        stack.Children.Add(ValueLine("Risk-ranked plan", "Build the plan to evaluate safe, review, and manual work.", "ExecutionPlanSummary"));
        stack.Children.Add(ValueLine("Queue state", "Idle", "ExecutionQueueState"));
        stack.Children.Add(ValueLine("Current stage", "Waiting", "ExecutionCurrentStage"));

        var progress = new ProgressBar
        {
            Height = 9,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 10, 0, 14),
            Tag = "ExecutionProgressBar"
        };
        stack.Children.Add(progress);

        stack.Children.Add(SectionTitle("Verification timeline"));
        stack.Children.Add(PipelineStep("1", "Prepare backup and evidence", "ExecutionStepPrepare"));
        stack.Children.Add(PipelineStep("2", "Read current WordPress values", "ExecutionStepRead"));
        stack.Children.Add(PipelineStep("3", "Apply approved changes", "ExecutionStepWrite"));
        stack.Children.Add(PipelineStep("4", "Read values back and verify", "ExecutionStepVerify"));
        stack.Children.Add(PipelineStep("5", "Record evidence and report", "ExecutionStepReport"));

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(ActionButton("Build plan", () => main.ExecutionCenter.BuildPlanCommand.Execute(null)));
        actions.Children.Add(ActionButton("Select ready", () => main.ExecutionCenter.SelectReadyCommand.Execute(null)));
        actions.Children.Add(ActionButton("Execute selected", async () =>
        {
            if (main.ExecutionCenter.ExecuteSelectedCommand.CanExecute(null))
                await main.ExecutionCenter.ExecuteSelectedCommand.ExecuteAsync(null);
        }));
        actions.Children.Add(ActionButton("Execute all ready", async () =>
        {
            if (main.ExecutionCenter.ExecuteAllReadyCommand.CanExecute(null))
                await main.ExecutionCenter.ExecuteAllReadyCommand.ExecuteAsync(null);
        }));
        actions.Children.Add(ActionButton("Retry failed", async () =>
        {
            if (main.ExecutionCenter.RetryFailedCommand.CanExecute(null))
                await main.ExecutionCenter.RetryFailedCommand.ExecuteAsync(null);
        }));
        actions.Children.Add(ActionButton("Rollback selected", async () =>
        {
            if (main.ExecutionCenter.RollbackSelectedCommand.CanExecute(null))
                await main.ExecutionCenter.RollbackSelectedCommand.ExecuteAsync(null);
        }));
        actions.Children.Add(ActionButton("Export report", () => ExportReport(main)));
        stack.Children.Add(actions);

        return shell;
    }

    private static Border Metric(string title, string value, string tag)
    {
        return new Border
        {
            Margin = new Thickness(3),
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 10, Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray) },
                    new TextBlock { Text = value, Tag = tag, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) }
                }
            }
        };
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Margin = new Thickness(0, 8, 0, 5),
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
    };

    private static Border ValueLine(string title, string value, string tag)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Tag = tag,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        return new Border
        {
            Margin = new Thickness(0, 3, 0, 3),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(7),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = stack
        };
    }

    private static Border PipelineStep(string number, string text, string tag)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Width = 25,
            Height = 25,
            CornerRadius = new CornerRadius(13),
            Background = ResourceBrush("BorderBrush", Brushes.LightGray),
            Child = new TextBlock
            {
                Text = number,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            }
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            Tag = tag,
            Margin = new Thickness(9, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        return new Border { Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(7), Child = row };
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(10, 7, 10, 7) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(10, 7, 10, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void RefreshConsole(Border console, MainWindowViewModel main)
    {
        console.Visibility = main.CurrentPage == "Execution Center" ? Visibility.Visible : Visibility.Collapsed;
        if (console.Visibility != Visibility.Visible) return;

        var vm = main.ExecutionCenter;
        SetText(console, "ExecutionReady", vm.ReadyCount.ToString());
        SetText(console, "ExecutionExecuted", vm.ExecutedCount.ToString());
        SetText(console, "ExecutionFailed", vm.FailedCount.ToString());
        SetText(console, "ExecutionBlocked", vm.BlockedCount.ToString());
        SetText(console, "ExecutionSelected", vm.SelectedCount.ToString());
        SetText(console, "ExecutionProgress", $"{Math.Clamp(vm.ProgressPercent, 0, 100)}%");
        SetText(console, "ExecutionPlanSummary", vm.PlanSummary);
        SetText(console, "ExecutionQueueState", vm.QueueState);
        SetText(console, "ExecutionCurrentStage", vm.CurrentStep);

        var bar = Find<ProgressBar>(console, "ExecutionProgressBar");
        if (bar is not null)
        {
            bar.Value = Math.Clamp(vm.ProgressPercent, 0, 100);
            bar.IsIndeterminate = vm.IsBusy && vm.ProgressPercent <= 0;
        }

        var progress = Math.Clamp(vm.ProgressPercent, 0, 100);
        SetPipeline(console, "ExecutionStepPrepare", progress >= 10, vm.IsBusy && progress < 25);
        SetPipeline(console, "ExecutionStepRead", progress >= 25, vm.IsBusy && progress is >= 25 and < 45);
        SetPipeline(console, "ExecutionStepWrite", progress >= 45, vm.IsBusy && progress is >= 45 and < 75);
        SetPipeline(console, "ExecutionStepVerify", progress >= 75, vm.IsBusy && progress is >= 75 and < 95);
        SetPipeline(console, "ExecutionStepReport", progress >= 95 || vm.ExecutedCount > 0, vm.IsBusy && progress >= 95);
    }

    private static void SetPipeline(DependencyObject root, string tag, bool complete, bool current)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is null) return;
        var original = text.Text.TrimStart('✓', '▶', '○', ' ');
        text.Text = complete ? $"✓  {original}" : current ? $"▶  {original}" : $"○  {original}";
        text.FontWeight = current ? FontWeights.Bold : FontWeights.Normal;
    }

    private static void ExportReport(MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        var vm = main.ExecutionCenter;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager",
            "Reports",
            "Execution");
        Directory.CreateDirectory(directory);

        var safeSite = string.Concat((site?.Name ?? "website").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(directory, $"Execution-{safeSite}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        var builder = new StringBuilder();
        builder.AppendLine("AI WordPress Manager - Execution & Verification Report");
        builder.AppendLine(new string('=', 60));
        builder.AppendLine($"Generated: {DateTime.Now:O}");
        builder.AppendLine($"Website: {site?.Name ?? "No site selected"}");
        builder.AppendLine($"URL: {site?.SiteUrl ?? "N/A"}");
        builder.AppendLine($"Queue state: {vm.QueueState}");
        builder.AppendLine($"Current step: {vm.CurrentStep}");
        builder.AppendLine($"Progress: {vm.ProgressPercent}%");
        builder.AppendLine();
        builder.AppendLine("Summary");
        builder.AppendLine($"Ready: {vm.ReadyCount}");
        builder.AppendLine($"Executed / verified: {vm.ExecutedCount}");
        builder.AppendLine($"Failed: {vm.FailedCount}");
        builder.AppendLine($"Blocked / manual: {vm.BlockedCount}");
        builder.AppendLine($"Pending approval: {vm.PendingApprovalCount}");
        builder.AppendLine($"Selected: {vm.SelectedCount}");
        builder.AppendLine();
        builder.AppendLine("Execution plan");
        builder.AppendLine(vm.PlanSummary);
        builder.AppendLine();
        builder.AppendLine("Latest status");
        builder.AppendLine(vm.StatusMessage);
        builder.AppendLine();
        builder.AppendLine("Operation timeline");
        foreach (var item in main.Operations.History.Take(100))
            builder.AppendLine(item.DisplayText.Replace(Environment.NewLine, " | "));

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
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
