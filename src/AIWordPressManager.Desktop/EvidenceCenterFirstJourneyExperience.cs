using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class EvidenceCenterFirstJourneyExperience
{
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
        var anchor = FindButton(window, main.EvidenceCenter.LoadCommand);
        var host = FindHost(anchor);
        if (host is null) return null;
        var existing = host.Children.OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "EvidenceCenterFirstJourney"));
        if (existing is not null) return existing;

        var card = new Border { Tag = "EvidenceCenterFirstJourney", Margin = new Thickness(0,0,0,14), Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "STEP 7 · VERIFY AND COMPLETE JOURNEY", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.DodgerBlue });
        var status = new TextBlock { Margin = new Thickness(0,6,0,10), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding("EvidenceCenter.FirstJourneyStatus"));
        root.Children.Add(status);
        var metrics = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,10) };
        metrics.SetBinding(TextBlock.TextProperty, new MultiBinding { StringFormat = "Artifacts: {0}   Receipts: {1}   Before: {2}   After: {3}   Verified pairs: {4}", Bindings = { new Binding("EvidenceCenter.TotalCount"), new Binding("EvidenceCenter.ReceiptCount"), new Binding("EvidenceCenter.BeforeCount"), new Binding("EvidenceCenter.AfterCount"), new Binding("EvidenceCenter.VerifiedPairCount") } });
        root.Children.Add(metrics);
        var requirements = new ItemsControl();
        requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("EvidenceCenter.FirstJourneyRequirements"));
        root.Children.Add(requirements);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,12,0,0) };
        actions.Children.Add(CreateButton("Refresh evidence", "EvidenceCenter.LoadCommand", null));
        actions.Children.Add(CreateButton("Open selected", "EvidenceCenter.OpenSelectedCommand", null));
        actions.Children.Add(CreateButton("Open evidence folder", "EvidenceCenter.OpenFolderCommand", null));
        var finish = CreateButton("Journey completed", "NavigateCommand", "Dashboard");
        finish.SetBinding(UIElement.IsEnabledProperty, new Binding("EvidenceCenter.IsFirstJourneyReady"));
        actions.Children.Add(finish);
        root.Children.Add(actions);
        card.Child = root;
        host.Children.Insert(0, card);
        return card;
    }

    private static Button CreateButton(string text, string command, string? parameter)
    {
        var button = new Button { Content = text, Margin = new Thickness(0,0,8,0), Padding = new Thickness(12,7,12,7) };
        button.SetBinding(Button.CommandProperty, new Binding(command));
        if (parameter is not null) button.CommandParameter = parameter;
        return button;
    }

    private static Button? FindButton(DependencyObject root, object command)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && ReferenceEquals(button.Command, command)) return button;
            var nested = FindButton(child, command);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static StackPanel? FindHost(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is StackPanel panel) return panel;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _card;
        private bool _isRefreshing;

        public void Attach()
        {
            main.PropertyChanged += OnChanged;
            main.EvidenceCenter.PropertyChanged += OnChanged;
            main.EvidenceCenter.Items.CollectionChanged += (_, _) => Refresh();
            window.Closed += OnClosed;
            window.Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try
            {
                main.EvidenceCenter.MergeExecutionReceipts();
                main.EvidenceCenter.RefreshFirstJourneyReadiness();
                _card ??= Install(window, main);
                if (_card is not null)
                {
                    var visible = main.CurrentPage.Equals("Evidence Center", StringComparison.OrdinalIgnoreCase);
                    _card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
                main.RefreshFirstJourneySidebar();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnChanged;
            main.EvidenceCenter.PropertyChanged -= OnChanged;
            window.Closed -= OnClosed;
        }
    }
}
