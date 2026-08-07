using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class FirstJourneyCompletionExperience
{
    private const int MaximumInstallAttempts = 6;
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
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
        var anchor = FindButtonForCommand(window, main.ContinueJourneyCommand)
            ?? FindButtonForCommand(window, main.StartOptimizationCommand);
        var host = FindStackPanelHost(anchor)
            ?? FindText(window, "Guided optimization workflow")?.Parent as StackPanel;
        if (host is null) return null;

        var existing = host.Children.OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "FirstJourneyCompletionSummary"));
        if (existing is not null) return existing;

        var card = new Border
        {
            Tag = "FirstJourneyCompletionSummary",
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "SuccessBrush", Brushes.SeaGreen),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "FIRST JOURNEY RESULT",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "SuccessBrush", Brushes.SeaGreen)
        });

        var title = new TextBlock { FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 4) };
        title.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneyCompletionTitle"));
        root.Children.Add(title);

        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        summary.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneyCompletionSummary"));
        root.Children.Add(summary);

        var evidence = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        evidence.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneyCompletionEvidence"));
        root.Children.Add(evidence);

        var receipt = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 12) };
        receipt.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneyCompletionReceipt"));
        root.Children.Add(receipt);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(CreateButton("Open final receipt", "OpenCompletedJourneyReceiptCommand"));
        actions.Children.Add(CreateButton("Refresh verification", "RefreshCompletedJourneyCommand"));
        actions.Children.Add(CreateNavigationButton("Review Evidence Center", "Evidence Center"));
        root.Children.Add(actions);

        card.Child = root;
        host.Children.Insert(0, card);
        return card;
    }

    private static Button CreateButton(string text, string commandPath)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 7, 12, 7) };
        button.SetBinding(Button.CommandProperty, new Binding(commandPath));
        return button;
    }

    private static Button CreateNavigationButton(string text, string target)
    {
        var button = CreateButton(text, "NavigateCommand");
        button.CommandParameter = target;
        return button;
    }

    private static Button? FindButtonForCommand(DependencyObject root, object expectedCommand)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
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

    private static TextBlock? FindText(DependencyObject root, string value)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text && string.Equals(text.Text, value, StringComparison.Ordinal)) return text;
            var nested = FindText(child, value);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _card;
        private bool _refreshing;
        private bool _installScheduled;
        private int _installAttempts;

        public void Attach()
        {
            main.PropertyChanged += OnChanged;
            main.EvidenceCenter.PropertyChanged += OnChanged;
            main.ExecutionCenter.PropertyChanged += OnChanged;
            window.Closed += OnClosed;
            ScheduleRefresh();
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e) => ScheduleRefresh();

        private void ScheduleRefresh()
        {
            if (_installScheduled) return;
            _installScheduled = true;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _installScheduled = false;
                Refresh();
            }), DispatcherPriority.Loaded);
        }

        private void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                main.RefreshFirstJourneyCompletion();
                _card ??= Install(window, main);
                if (_card is null && _installAttempts++ < MaximumInstallAttempts)
                {
                    window.Dispatcher.BeginInvoke(new Action(ScheduleRefresh), DispatcherPriority.ContextIdle);
                    return;
                }

                if (_card is not null)
                {
                    var visible = main.CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase) && main.IsFirstJourneyCompleted;
                    _card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    _card.IsHitTestVisible = visible;
                }
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnChanged;
            main.EvidenceCenter.PropertyChanged -= OnChanged;
            main.ExecutionCenter.PropertyChanged -= OnChanged;
            window.Closed -= OnClosed;
        }
    }
}
