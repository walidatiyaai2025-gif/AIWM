using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class BulkExecutionPresetsExperience
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

        var state = new State(main, window, root);
        Attached.Add(window, state);

        main.PropertyChanged += state.OnMainPropertyChanged;
        window.Closed += state.OnWindowClosed;
        Refresh(root, state);
    }

    private static void Refresh(Grid root, State state)
    {
        var actionBar = FindByTag<Border>(root, "PrimaryWorkActionBar");
        if (actionBar?.Child is not Grid barGrid) return;

        state.Panel ??= BuildPanel(state);
        if (state.Panel.Parent is null)
        {
            Grid.SetColumn(state.Panel, 1);
            barGrid.Children.Add(state.Panel);
        }

        var visible = state.Main.CurrentPage.Equals("Execution Center", StringComparison.OrdinalIgnoreCase);
        state.Panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        state.Panel.IsHitTestVisible = visible;
        if (visible) UpdateSummary(state);
    }

    private static Border BuildPanel(State state)
    {
        var shell = new Border
        {
            Tag = "BulkExecutionPresetBar",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(7),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1)
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        shell.Child = panel;

        state.Summary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 11,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        panel.Children.Add(state.Summary);

        var presets = new ComboBox
        {
            Width = 132,
            Height = 25,
            Margin = new Thickness(0, 0, 6, 0),
            ItemsSource = Enum.GetValues<BulkPreset>().Select(ToLabel).ToArray(),
            SelectedIndex = 0,
            ToolTip = "Choose a safe bulk execution category."
        };
        presets.SelectionChanged += (_, _) =>
        {
            state.Preset = (BulkPreset)Math.Max(0, presets.SelectedIndex);
            state.PreviewAccepted = false;
            UpdateSummary(state);
        };
        panel.Children.Add(presets);

        panel.Children.Add(Button("Preview", async () =>
        {
            await EnsureLoadedAsync(state);
            state.PreviewAccepted = ShowPreview(state, requireConfirmation: false);
            UpdateSummary(state);
        }));
        panel.Children.Add(Button("Select", async () =>
        {
            await EnsureLoadedAsync(state);
            SelectPreset(state);
        }));
        panel.Children.Add(Button("Execute preset", async () =>
        {
            await EnsureLoadedAsync(state);
            if (!ShowPreview(state, requireConfirmation: true)) return;

            SelectPreset(state);
            if (state.Main.ExecutionCenter.SelectedItems.Count == 0) return;
            if (state.Main.ExecutionCenter.ExecuteSelectedCommand.CanExecute(null))
                await state.Main.ExecutionCenter.ExecuteSelectedCommand.ExecuteAsync(null);
            UpdateSummary(state);
        }, primary: true));
        panel.Children.Add(Button("Clear", () =>
        {
            state.Main.ExecutionCenter.ClearSelectionCommand.Execute(null);
            state.PreviewAccepted = false;
            UpdateSummary(state);
            return Task.CompletedTask;
        }));

        return shell;
    }

    private static async Task EnsureLoadedAsync(State state)
    {
        var center = state.Main.ExecutionCenter;
        if (center.Items.Count == 0 && center.LoadCommand.CanExecute(null))
            await center.LoadCommand.ExecuteAsync(null);
    }

    private static void SelectPreset(State state)
    {
        var center = state.Main.ExecutionCenter;
        center.SelectedItems.Clear();
        foreach (var item in center.Items.Where(x => Matches(x, state.Preset)))
            center.SelectedItems.Add(item);
        center.SelectedItem = center.SelectedItems.FirstOrDefault();
        UpdateSummary(state);
    }

    private static bool ShowPreview(State state, bool requireConfirmation)
    {
        var center = state.Main.ExecutionCenter;
        var all = center.Items.Where(x => MatchesCategory(x, state.Preset)).ToArray();
        var eligible = all.Where(IsSafeEligible).ToArray();
        var ready = eligible.Count(x => x.CanExecute);
        var needsApproval = eligible.Count(x => !x.CanExecute && x.CanApprove);
        var highRisk = all.Count(x => x.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase));
        var staging = all.Count(x => x.RequiresStaging);
        var unsupported = all.Count(x => !x.CanExecute && !x.CanApprove);
        var executed = all.Count(x => x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));

        var types = eligible
            .GroupBy(x => string.IsNullOrWhiteSpace(x.ChangeType) ? "Unknown" : x.ChangeType)
            .OrderByDescending(x => x.Count())
            .Take(8)
            .Select(x => $"• {x.Key}: {x.Count()}");

        var message = new StringBuilder()
            .AppendLine($"Preset: {ToLabel(state.Preset)}")
            .AppendLine($"Safe eligible: {eligible.Length}")
            .AppendLine($"Ready now: {ready}")
            .AppendLine($"Needs approval first: {needsApproval}")
            .AppendLine()
            .AppendLine("Excluded by safety gate:")
            .AppendLine($"• High risk: {highRisk}")
            .AppendLine($"• Requires staging: {staging}")
            .AppendLine($"• Unsupported/manual: {unsupported}")
            .AppendLine($"• Already executed: {executed}")
            .AppendLine()
            .AppendLine("Top included change types:")
            .AppendLine(types.Any() ? string.Join(Environment.NewLine, types) : "• None")
            .AppendLine()
            .AppendLine("Execution uses backup, WordPress update, read-back verification and job history.")
            .ToString();

        if (eligible.Length == 0)
        {
            MessageBox.Show(state.Window, message, "Preset preview — nothing safe to execute", MessageBoxButton.OK, MessageBoxImage.Information);
            state.PreviewAccepted = false;
            return false;
        }

        if (!requireConfirmation)
        {
            MessageBox.Show(state.Window, message, "Execution preset preview", MessageBoxButton.OK, MessageBoxImage.Information);
            state.PreviewAccepted = true;
            return true;
        }

        var result = MessageBox.Show(
            state.Window,
            message + Environment.NewLine + "Continue with this safe batch?",
            "Confirm bulk execution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        state.PreviewAccepted = result == MessageBoxResult.Yes;
        return state.PreviewAccepted;
    }

    private static bool Matches(ApprovedChangeExecutionItem item, BulkPreset preset) =>
        MatchesCategory(item, preset) && IsSafeEligible(item);

    private static bool IsSafeEligible(ApprovedChangeExecutionItem item)
    {
        if (item.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase)) return false;
        if (item.RequiresStaging || item.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase)) return false;
        return item.CanExecute || item.CanApprove;
    }

    private static bool MatchesCategory(ApprovedChangeExecutionItem item, BulkPreset preset)
    {
        var type = item.ChangeType ?? string.Empty;
        return preset switch
        {
            BulkPreset.LowRisk => item.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase),
            BulkPreset.Seo => ContainsAny(type, "SEO", "TITLE", "SLUG", "H1", "CANONICAL"),
            BulkPreset.Media => ContainsAny(type, "MEDIA", "IMAGE", "ALT", "CAPTION"),
            BulkPreset.Metadata => ContainsAny(type, "META", "DESCRIPTION", "EXCERPT", "CATEGORY", "TAG"),
            BulkPreset.AllReady => item.CanExecute,
            _ => false
        };
    }

    private static void UpdateSummary(State state)
    {
        if (state.Summary is null) return;
        var center = state.Main.ExecutionCenter;
        var eligible = center.Items.Count(x => Matches(x, state.Preset));
        var excluded = center.Items.Count(x => MatchesCategory(x, state.Preset) && !IsSafeEligible(x));
        state.Summary.Text = $"{ToLabel(state.Preset)}: {eligible} safe · {excluded} excluded · Selected: {center.SelectedItems.Count}";
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string ToLabel(BulkPreset preset) => preset switch
    {
        BulkPreset.LowRisk => "Low risk",
        BulkPreset.Seo => "SEO",
        BulkPreset.Media => "Media",
        BulkPreset.Metadata => "Metadata",
        BulkPreset.AllReady => "All ready",
        _ => "Low risk"
    };

    private static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Height = 25,
            MinWidth = primary ? 105 : 58,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(8, 2, 8, 2),
            FontSize = 11,
            Background = primary ? Brush("PrimaryBrush", Brushes.Teal) : Brushes.Transparent,
            Foreground = primary ? Brushes.White : Brush("TextPrimaryBrush", Brushes.Black)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var result = FindByTag<T>(child, tag);
            if (result is not null) return result;
        }
        return null;
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed class State(MainWindowViewModel main, MainWindow window, Grid root)
    {
        public MainWindowViewModel Main { get; } = main;
        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public Border? Panel { get; set; }
        public TextBlock? Summary { get; set; }
        public BulkPreset Preset { get; set; } = BulkPreset.LowRisk;
        public bool PreviewAccepted { get; set; }

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                Refresh(Root, this);
        }

        public void OnWindowClosed(object? sender, EventArgs e)
        {
            Main.PropertyChanged -= OnMainPropertyChanged;
            Window.Closed -= OnWindowClosed;
        }
    }

    private enum BulkPreset
    {
        LowRisk,
        Seo,
        Media,
        Metadata,
        AllReady
    }
}
