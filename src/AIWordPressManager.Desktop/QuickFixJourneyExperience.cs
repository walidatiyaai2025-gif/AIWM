using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class QuickFixJourneyExperience
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
        var panel = BuildPanel(main);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 54);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)) Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();
        main.Explorer.PropertyChanged += (_, _) => Refresh();
        main.SuggestedChanges.PropertyChanged += (_, _) => Refresh();
        main.SuggestedChanges.Items.CollectionChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border BuildPanel(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Width = 430,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 18, 22, 0),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "QuickFixJourneyPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(Title("Journey completion"));
        stack.Children.Add(Text("JourneyScore", true));

        var progress = new ProgressBar
        {
            Tag = "JourneyProgress",
            Height = 9,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 8, 0, 8)
        };
        stack.Children.Add(progress);
        stack.Children.Add(Text("JourneyNext", false));

        var journeyActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 14) };
        var next = Button("Continue journey", async () => await ContinueJourneyAsync(main));
        next.Tag = "JourneyContinue";
        journeyActions.Children.Add(next);
        var mission = Button("Mission Control", async () => await main.NavigateCommand.ExecuteAsync("Dashboard"));
        journeyActions.Children.Add(mission);
        stack.Children.Add(journeyActions);

        stack.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });
        stack.Children.Add(Title("Quick Fix Queue"));
        stack.Children.Add(Text("QuickFixSummary", true));
        stack.Children.Add(Text("QuickFixDetail", false));

        var quickActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var review = Button("Review next fix", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
            main.SuggestedChanges.SelectedItem = GetQuickFixes(main).FirstOrDefault();
        });
        review.Tag = "QuickFixReview";
        quickActions.Children.Add(review);

        var copy = Button("Copy queue", () =>
        {
            var fixes = GetQuickFixes(main).Take(20).ToArray();
            var text = fixes.Length == 0
                ? "No low-risk pending fixes are currently available."
                : string.Join(Environment.NewLine, fixes.Select((x, i) =>
                    $"{i + 1}. {x.ObjectType} {x.ObjectId} — {x.ChangeType} — Confidence {x.Confidence:P0} — {x.CleanReason}"));
            Clipboard.SetText(text);
            return Task.CompletedTask;
        });
        copy.Tag = "QuickFixCopy";
        quickActions.Children.Add(copy);

        var generate = Button("Generate fixes", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
        });
        generate.Tag = "QuickFixGenerate";
        quickActions.Children.Add(generate);
        stack.Children.Add(quickActions);
        return shell;
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        panel.Visibility = main.CurrentPage is "Dashboard" or "Suggested Changes" or "SEO Audit"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var state = CalculateJourney(main);
        SetText(panel, "JourneyScore", $"{state.Percent}% complete • {state.CompletedStages}/6 stages completed");
        SetText(panel, "JourneyNext", state.NextMessage);
        var progress = Find<ProgressBar>(panel, "JourneyProgress");
        if (progress is not null) progress.Value = state.Percent;

        var fixes = GetQuickFixes(main).ToArray();
        var first = fixes.FirstOrDefault();
        SetText(panel, "QuickFixSummary", fixes.Length == 0
            ? "No low-risk pending fixes are ready."
            : $"{fixes.Length} low-risk pending fix(es) sorted by confidence");
        SetText(panel, "QuickFixDetail", first is null
            ? "Generate proposals or complete the current review queue."
            : $"Next: {first.ChangeType} • {first.ObjectType} {first.ObjectId}\nConfidence {first.Confidence:P0} • {first.CleanReason}");

        SetEnabled(panel, "JourneyContinue", !main.IsOperationRunning);
        SetEnabled(panel, "QuickFixReview", first is not null && !main.IsOperationRunning);
        SetEnabled(panel, "QuickFixCopy", fixes.Length > 0);
        SetEnabled(panel, "QuickFixGenerate", main.Sites.SelectedSite is not null && !main.IsOperationRunning);
    }

    private static IEnumerable<SuggestedChangeItem> GetQuickFixes(MainWindowViewModel main) =>
        main.SuggestedChanges.Items
            .Where(x => x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase)
                        && x.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase)
                        && !x.RequiresStaging
                        && x.ExecutionStatus is not "Executed" and not "Executing")
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.CanApplyDirectly)
            .ThenByDescending(x => x.CreatedAtUtc);

    private static JourneyState CalculateJourney(MainWindowViewModel main)
    {
        var selected = main.Sites.SelectedSite is not null;
        var synchronized = main.Explorer.LoadedItemsCount > 0;
        var items = main.SuggestedChanges.Items.ToArray();
        var analyzed = items.Length > 0;
        var reviewed = items.Any(x => !x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        var approved = items.Any(x => x.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase));
        var executed = items.Any(x => x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));

        var stages = new[] { selected, synchronized, analyzed, reviewed, approved, executed };
        var complete = stages.Count(x => x);
        var percent = (int)Math.Round(complete / 6d * 100d);
        var next = !selected ? "Select or add a website to begin."
            : !synchronized ? "Synchronize the selected website."
            : !analyzed ? "Run the audit and generate proposals."
            : !reviewed ? "Review pending proposals and approve or reject them."
            : !approved ? "Approve at least one safe proposal for execution."
            : !executed ? "Open Execution Center and execute approved changes."
            : "The core journey is complete. Rerun the audit to measure improvement.";
        return new JourneyState(percent, complete, next);
    }

    private static async Task ContinueJourneyAsync(MainWindowViewModel main)
    {
        var selected = main.Sites.SelectedSite is not null;
        var synchronized = main.Explorer.LoadedItemsCount > 0;
        var items = main.SuggestedChanges.Items.ToArray();
        var analyzed = items.Length > 0;
        var reviewed = items.Any(x => !x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        var approved = items.Any(x => x.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase));
        var executed = items.Any(x => x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));

        if (!selected) { await main.NavigateCommand.ExecuteAsync("Sites"); return; }
        if (!synchronized)
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            await main.Explorer.SynchronizeNowAsync();
            return;
        }
        if (!analyzed)
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
            return;
        }
        if (!reviewed || !approved)
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
            main.SuggestedChanges.SelectedItem = GetQuickFixes(main).FirstOrDefault() ?? items.FirstOrDefault();
            return;
        }
        if (!executed) { await main.NavigateCommand.ExecuteAsync("Execution Center"); return; }
        await main.NavigateCommand.ExecuteAsync("SEO Audit");
    }

    private static TextBlock Title(string value) => new()
    {
        Text = value,
        FontSize = 17,
        FontWeight = FontWeights.Bold,
        Foreground = Brush("TextPrimaryBrush", Brushes.Black)
    };

    private static TextBlock Text(string tag, bool bold) => new()
    {
        Tag = tag,
        Margin = new Thickness(0, 5, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = bold ? Brush("TextPrimaryBrush", Brushes.Black) : Brush("TextSecondaryBrush", Brushes.DimGray)
    };

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(11, 7, 11, 7)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
    }

    private static void SetEnabled(DependencyObject root, string tag, bool value)
    {
        var button = Find<Button>(root, tag);
        if (button is not null) button.IsEnabled = value;
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

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record JourneyState(int Percent, int CompletedStages, string NextMessage);
}
