using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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

        var state = new State(main);
        Attached.Add(window, state);

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        timer.Tick += (_, _) => Refresh(root, state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
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
        if (!visible) return;

        UpdateSummary(state);
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
            Width = 145,
            Height = 25,
            Margin = new Thickness(0, 0, 6, 0),
            ItemsSource = Enum.GetValues<BulkPreset>().Select(ToLabel).ToArray(),
            SelectedIndex = 0,
            ToolTip = "Choose which approved or safely approvable actions should be selected."
        };
        presets.SelectionChanged += (_, _) =>
        {
            state.Preset = (BulkPreset)Math.Max(0, presets.SelectedIndex);
            UpdateSummary(state);
        };
        panel.Children.Add(presets);

        panel.Children.Add(Button("Select", async () =>
        {
            await EnsureLoadedAsync(state);
            SelectPreset(state);
        }));
        panel.Children.Add(Button("Execute preset", async () =>
        {
            await EnsureLoadedAsync(state);
            SelectPreset(state);
            if (state.Main.ExecutionCenter.SelectedItems.Count == 0) return;
            if (state.Main.ExecutionCenter.ExecuteSelectedCommand.CanExecute(null))
                await state.Main.ExecutionCenter.ExecuteSelectedCommand.ExecuteAsync(null);
        }, primary: true));
        panel.Children.Add(Button("Clear", () =>
        {
            state.Main.ExecutionCenter.ClearSelectionCommand.Execute(null);
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

    private static bool Matches(ApprovedChangeExecutionItem item, BulkPreset preset)
    {
        if (item.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase)) return false;
        if (item.RequiresStaging || item.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase)) return false;
        if (!item.CanExecute && !item.CanApprove) return false;

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
        state.Summary.Text = $"{ToLabel(state.Preset)}: {eligible} eligible · Selected: {center.SelectedItems.Count}";
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

    private sealed class State(MainWindowViewModel main)
    {
        public MainWindowViewModel Main { get; } = main;
        public Border? Panel { get; set; }
        public TextBlock? Summary { get; set; }
        public BulkPreset Preset { get; set; } = BulkPreset.LowRisk;
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
