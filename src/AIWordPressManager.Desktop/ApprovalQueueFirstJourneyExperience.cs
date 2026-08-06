using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ApprovalQueueFirstJourneyExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded), true);
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

    private static Border? Install(MainWindow window, MainWindowViewModel main)
    {
        var anchor = FindButtonForCommand(window, main.SuggestedChanges.BulkApproveCommand)
            ?? FindButtonForCommand(window, main.SuggestedChanges.ApproveCommand);
        var host = FindStackPanelHost(anchor);
        if (host is null) return null;
        var existing = host.Children.OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "ApprovalQueueFirstJourney"));
        if (existing is not null) return existing;

        var card = new Border
        {
            Tag = "ApprovalQueueFirstJourney",
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.LightGray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke)
        };
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "STEP 5 · APPROVE EXECUTION QUEUE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
        });
        var status = new TextBlock { Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding("SuggestedChanges.ApprovalJourneyStatus"));
        root.Children.Add(status);

        var metrics = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        metrics.SetBinding(TextBlock.TextProperty, new MultiBinding
        {
            StringFormat = "Pending: {0}   Approved: {1}   Rejected: {2}   Execution ready: {3}",
            Bindings =
            {
                new Binding("SuggestedChanges.ApprovalPendingCount"),
                new Binding("SuggestedChanges.ApprovalApprovedCount"),
                new Binding("SuggestedChanges.ApprovalRejectedCount"),
                new Binding("SuggestedChanges.ExecutionReadyCount")
            }
        });
        root.Children.Add(metrics);

        var requirements = new ItemsControl();
        requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("SuggestedChanges.ApprovalJourneyRequirements"));
        var template = new DataTemplate();
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new Binding("StatusIcon"));
        icon.SetValue(FrameworkElement.WidthProperty, 24d);
        icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Title"));
        panel.AppendChild(icon);
        panel.AppendChild(text);
        template.VisualTree = panel;
        requirements.ItemTemplate = template;
        root.Children.Add(requirements);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(CreateButton("Refresh queue", "SuggestedChanges.RefreshCommand", null));
        actions.Children.Add(CreateButton("Approve selected", "SuggestedChanges.BulkApproveCommand", null));
        var continueButton = CreateButton("Continue to Execution Center", "NavigateCommand", "Execution Center");
        continueButton.SetBinding(UIElement.IsEnabledProperty, new Binding("SuggestedChanges.IsApprovalJourneyReady"));
        actions.Children.Add(continueButton);
        root.Children.Add(actions);
        card.Child = root;
        host.Children.Insert(0, card);
        return card;
    }

    private static Button CreateButton(string text, string commandPath, string? parameter)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 7, 12, 7) };
        button.SetBinding(Button.CommandProperty, new Binding(commandPath));
        if (parameter is not null) button.CommandParameter = parameter;
        return button;
    }

    private static Button? FindButtonForCommand(DependencyObject root, object expectedCommand)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button && ReferenceEquals(button.Command, expectedCommand)) return button;
            var nested = FindButtonForCommand(child, expectedCommand);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static StackPanel? FindStackPanelHost(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is StackPanel panel) return panel;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _card;
        private bool _refreshPending;

        public void Attach()
        {
            main.PropertyChanged += OnChanged;
            main.SuggestedChanges.PropertyChanged += OnChanged;
            main.SuggestedChanges.Items.CollectionChanged += OnCollectionChanged;
            window.Closed += OnClosed;
            QueueRefresh();
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

        private void QueueRefresh()
        {
            if (_refreshPending || window.Dispatcher.HasShutdownStarted) return;
            _refreshPending = true;
            window.Dispatcher.BeginInvoke(new Action(async () =>
            {
                _refreshPending = false;
                await main.SuggestedChanges.RefreshApprovalJourneyReadinessAsync();
                _card ??= Install(window, main);
                if (_card is not null)
                {
                    var visible = main.CurrentPage.Equals("Approval Queue", StringComparison.OrdinalIgnoreCase);
                    _card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    _card.IsHitTestVisible = visible;
                }
            }));
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnChanged;
            main.SuggestedChanges.PropertyChanged -= OnChanged;
            main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            window.Closed -= OnClosed;
        }
    }
}
