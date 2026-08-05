using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class AiCopilotInboxExperience
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
        Panel.SetZIndex(panel, 66);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        void OnMainChanged(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning))
            {
                Refresh();
            }
        }
        void OnSuggestionsChanged(object? _, PropertyChangedEventArgs __) => Refresh();
        void OnSiteChanged(object? _, EventArgs __) => Refresh();

        main.PropertyChanged += OnMainChanged;
        main.SuggestedChanges.PropertyChanged += OnSuggestionsChanged;
        main.Sites.SelectedSiteChanged += OnSiteChanged;
        window.Closed += (_, _) =>
        {
            main.PropertyChanged -= OnMainChanged;
            main.SuggestedChanges.PropertyChanged -= OnSuggestionsChanged;
            main.Sites.SelectedSiteChanged -= OnSiteChanged;
        };

        Refresh();
    }

    private static Border CreatePanel(MainWindowViewModel main)
    {
        var panel = new Border
        {
            Width = 370,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 18, 18),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("PrimaryBrush", Brushes.Teal),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "AiInboxRoot"
        };

        var stack = new StackPanel();
        panel.Child = stack;

        stack.Children.Add(new TextBlock
        {
            Text = "AI Copilot Inbox",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Highest-impact, lowest-risk action for the current website.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });

        stack.Children.Add(new TextBlock
        {
            Tag = "AiInboxTitle",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "AiInboxMeta",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = ResourceBrush("PrimaryBrush", Brushes.Teal)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "AiInboxReason",
            Margin = new Thickness(0, 7, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 70,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });

        var actions = new WrapPanel();
        var primary = ActionButton("Open AI Inbox", async () => await ExecutePrimaryAsync(main));
        primary.Tag = "AiInboxPrimary";
        actions.Children.Add(primary);

        var sort = ActionButton("Smart sort", () =>
        {
            main.SuggestedChanges.ApplySmartQueue();
            return Task.CompletedTask;
        });
        sort.Tag = "AiInboxSort";
        actions.Children.Add(sort);

        var explain = ActionButton("Explain", async () =>
        {
            var item = main.SuggestedChanges.GetTopAiInboxItem();
            if (item is null) return;
            main.SuggestedChanges.SelectedItem = item;
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.ExplainCommand.CanExecute(item))
                await main.SuggestedChanges.ExplainCommand.ExecuteAsync(item);
        });
        explain.Tag = "AiInboxExplain";
        actions.Children.Add(explain);

        stack.Children.Add(actions);
        return panel;
    }

    private static async Task ExecutePrimaryAsync(MainWindowViewModel main)
    {
        if (main.Sites.SelectedSite is null)
        {
            await main.NavigateCommand.ExecuteAsync("Sites");
            return;
        }

        await main.NavigateCommand.ExecuteAsync("Suggested Changes");

        if (main.SuggestedChanges.Items.Count == 0)
        {
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
        }
        else
        {
            main.SuggestedChanges.ApplySmartQueue();
            await main.SuggestedChanges.LoadAsync();
            main.SuggestedChanges.ApplySmartQueue();
        }
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        var visiblePages = new[] { "Dashboard", "Suggested Changes", "SEO Audit", "AI Studio" };
        panel.Visibility = main.Sites.SelectedSite is not null && visiblePages.Contains(main.CurrentPage)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var title = Find<TextBlock>(panel, "AiInboxTitle");
        var meta = Find<TextBlock>(panel, "AiInboxMeta");
        var reason = Find<TextBlock>(panel, "AiInboxReason");
        var primary = Find<Button>(panel, "AiInboxPrimary");
        var sort = Find<Button>(panel, "AiInboxSort");
        var explain = Find<Button>(panel, "AiInboxExplain");

        if (main.SuggestedChanges.IsBusy)
        {
            if (title is not null) title.Text = "AI is preparing the inbox…";
            if (meta is not null) meta.Text = main.SuggestedChanges.StatusMessage;
            if (reason is not null) reason.Text = "The queue will be prioritized when generation finishes.";
            SetEnabled(false, primary, sort, explain);
            return;
        }

        var item = main.SuggestedChanges.GetTopAiInboxItem();
        if (item is null)
        {
            if (title is not null) title.Text = main.SuggestedChanges.Items.Count == 0
                ? "Generate AI suggestions"
                : "No pending AI review";
            if (meta is not null) meta.Text = main.SuggestedChanges.Items.Count == 0
                ? "Audit results can be converted into actionable proposals."
                : $"Approved: {main.SuggestedChanges.ApprovedCount} • Rejected: {main.SuggestedChanges.RejectedCount}";
            if (reason is not null) reason.Text = main.SuggestedChanges.Items.Count == 0
                ? "Create the first prioritized queue for this website."
                : "The pending queue is clear. Continue with Execution Center when approved changes exist.";
            if (primary is not null) primary.Content = main.SuggestedChanges.Items.Count == 0 ? "Generate inbox" : "Open review";
            SetEnabled(!main.IsOperationRunning, primary);
            SetEnabled(main.SuggestedChanges.Items.Count > 0, sort);
            SetEnabled(false, explain);
            return;
        }

        var confidence = main.SuggestedChanges.EstimateAiConfidence(item);
        var confidenceLabel = main.SuggestedChanges.GetAiConfidenceLabel(item);
        var priority = main.SuggestedChanges.GetAiPriorityLabel(item);

        if (title is not null) title.Text = $"{priority} priority • {item.ChangeType}";
        if (meta is not null) meta.Text = $"Confidence {confidence}% ({confidenceLabel}) • Risk {item.RiskLevel} • {item.ObjectType} #{item.ObjectId}";
        if (reason is not null) reason.Text = item.CleanReason;
        if (primary is not null) primary.Content = "Review top action";
        SetEnabled(!main.IsOperationRunning, primary, sort, explain);
    }

    private static void SetEnabled(bool enabled, params Button?[] buttons)
    {
        foreach (var button in buttons)
            if (button is not null) button.IsEnabled = enabled;
    }

    private static Button ActionButton(string text, Func<Task> action)
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
