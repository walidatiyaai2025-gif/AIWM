using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class SuggestedChangesFirstJourneyExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded),
            true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main) return;

        var state = new State(window, main);
        Attached.Add(window, state);
        state.Attach();
    }

    private static Border? InstallPanel(MainWindow window, MainWindowViewModel main)
    {
        var marker = FindButtonForCommand(window, main.SuggestedChanges.GenerateCommand);
        if (marker?.Parent is not Panel parent) return null;

        var existing = parent.Children.OfType<Border>()
            .FirstOrDefault(element => Equals(element.Tag, "SuggestedChangesFirstJourneyPanel"));
        if (existing is not null) return existing;

        var panel = new Border
        {
            Tag = "SuggestedChangesFirstJourneyPanel",
            Margin = new Thickness(0, 12, 0, 12),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.LightGray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "STEP 4 · REVIEW SUGGESTED CHANGES",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
        });

        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 12), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding("SuggestedChanges.FirstJourneyStatus"));
        root.Children.Add(status);

        var metrics = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
        metrics.Children.Add(CreateMetric("Proposals", "SuggestedChanges.Items.Count"));
        metrics.Children.Add(CreateMetric("Reviewed", "SuggestedChanges.FirstJourneyReviewedCount"));
        metrics.Children.Add(CreateMetric("Pending", "SuggestedChanges.PendingCount"));
        metrics.Children.Add(CreateMetric("High risk", "SuggestedChanges.HighRiskCount"));
        metrics.Children.Add(CreateMetric("Staging", "SuggestedChanges.StagingCount"));
        root.Children.Add(metrics);

        var requirements = new ItemsControl();
        requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SuggestedChanges.FirstJourneyRequirements"));
        requirements.ItemTemplate = CreateRequirementTemplate();
        root.Children.Add(requirements);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var generate = new Button { Content = "Generate proposals", MinWidth = 140, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        generate.SetBinding(Button.CommandProperty, new Binding("SuggestedChanges.GenerateCommand"));
        actions.Children.Add(generate);

        var refresh = new Button { Content = "Refresh review", MinWidth = 120, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0) };
        refresh.SetBinding(Button.CommandProperty, new Binding("SuggestedChanges.RefreshCommand"));
        actions.Children.Add(refresh);

        var next = new Button { Content = "Continue to Approval Queue", MinWidth = 190, Padding = new Thickness(14, 8, 14, 8), CommandParameter = "Approval Queue" };
        next.SetBinding(Button.CommandProperty, new Binding("NavigateCommand"));
        next.SetBinding(UIElement.IsEnabledProperty, new Binding("SuggestedChanges.IsFirstJourneyReady"));
        actions.Children.Add(next);
        root.Children.Add(actions);

        panel.Child = root;
        parent.Children.Insert(Math.Max(0, parent.Children.IndexOf(marker)), panel);
        return panel;
    }

    private static Border CreateMetric(string label, string path)
    {
        var card = new Border { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        var stack = new StackPanel();
        var value = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold };
        value.SetBinding(TextBlock.TextProperty, new Binding(path));
        stack.Children.Add(value);
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, Opacity = 0.72 });
        card.Child = stack;
        return card;
    }

    private static DataTemplate CreateRequirementTemplate()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3));

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding("StatusIcon"));
        icon.SetValue(TextBlock.WidthProperty, 24d);
        icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        row.AppendChild(icon);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.WidthProperty, 170d);
        row.AppendChild(title);

        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new Binding("Detail"));
        detail.SetValue(TextBlock.OpacityProperty, 0.72d);
        detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        row.AppendChild(detail);
        return new DataTemplate { VisualTree = row };
    }

    private static Button? FindButtonForCommand(DependencyObject parent, object expectedCommand)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && ReferenceEquals(button.Command, expectedCommand)) return button;
            var nested = FindButtonForCommand(child, expectedCommand);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _panel;

        public void Attach()
        {
            main.SuggestedChanges.Items.CollectionChanged += OnChanged;
            main.SuggestedChanges.PropertyChanged += OnPropertyChanged;
            main.PropertyChanged += OnMainPropertyChanged;
            window.Closed += OnClosed;
            window.Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void OnChanged(object? sender, EventArgs e) => Refresh();
        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage)) Refresh();
        }

        private void Refresh()
        {
            main.SuggestedChanges.RefreshFirstJourneyReadiness();
            _panel ??= InstallPanel(window, main);
            if (_panel is not null)
                _panel.Visibility = main.CurrentPage.Equals("Suggested Changes", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.SuggestedChanges.Items.CollectionChanged -= OnChanged;
            main.SuggestedChanges.PropertyChanged -= OnPropertyChanged;
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Closed -= OnClosed;
        }
    }
}
