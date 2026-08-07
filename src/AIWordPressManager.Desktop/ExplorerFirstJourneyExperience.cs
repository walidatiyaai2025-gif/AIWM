using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ExplorerFirstJourneyExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded), true);
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

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _panel;

        public void Attach()
        {
            window.Closed += OnClosed;
            main.PropertyChanged += OnMainPropertyChanged;
            window.Dispatcher.BeginInvoke(new Action(Install));
        }

        private void Install()
        {
            _panel ??= InstallPanel(window, main);
            ApplyVisibility();
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
            {
                if (_panel is null) Install();
                ApplyVisibility();
            }
        }

        private void ApplyVisibility()
        {
            if (_panel is null) return;
            var visible = main.CurrentPage.Equals("WordPress Explorer", StringComparison.OrdinalIgnoreCase);
            _panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _panel.IsHitTestVisible = visible;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Closed -= OnClosed;
        }
    }

    private static Border? InstallPanel(MainWindow window, MainWindowViewModel main)
    {
        var syncButton = FindButtonForCommand(window, main.Explorer.RefreshCommand);
        if (syncButton is null) return null;
        var host = FindHostPanel(syncButton);
        if (host is null) return null;

        var existing = host.Children.OfType<Border>().FirstOrDefault(x => Equals(x.Tag, "ExplorerFirstJourney"));
        if (existing is not null) return existing;

        var card = new Border
        {
            Tag = "ExplorerFirstJourney",
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.Gray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.Transparent)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "STEP 2 · COMPLETE WORDPRESS SNAPSHOT",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
        });

        var status = new TextBlock { Margin = new Thickness(0, 5, 0, 10), TextWrapping = TextWrapping.Wrap };
        status.SetBinding(TextBlock.TextProperty, new Binding("Explorer.FirstJourneyStatus"));
        root.Children.Add(status);

        var progress = new ProgressBar { Height = 7, Maximum = 100, Margin = new Thickness(0, 0, 0, 10) };
        progress.SetBinding(ProgressBar.ValueProperty, new Binding("Explorer.ProgressPercent"));
        root.Children.Add(progress);

        var requirements = new ItemsControl();
        requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Explorer.FirstJourneyRequirements"));
        requirements.ItemTemplate = BuildRequirementTemplate();
        root.Children.Add(requirements);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(BuildButton("Synchronize now", "Explorer.RefreshCommand", null, 140));
        actions.Children.Add(BuildButton("Cancel", "Explorer.CancelCommand", null, 90));
        actions.Children.Add(BuildButton("Continue to SEO Audit", "NavigateCommand", "SEO Audit", 170, "Explorer.IsFirstJourneyReady"));
        root.Children.Add(actions);

        card.Child = root;
        host.Children.Insert(0, card);
        return card;
    }

    private static Button BuildButton(string text, string commandPath, string? parameter, double minWidth, string? enabledPath = null)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        button.SetBinding(Button.CommandProperty, new Binding(commandPath));
        if (parameter is not null) button.CommandParameter = parameter;
        if (enabledPath is not null) button.SetBinding(UIElement.IsEnabledProperty, new Binding(enabledPath));
        return button;
    }

    private static DataTemplate BuildRequirementTemplate()
    {
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5));

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(FrameworkElement.WidthProperty, 24d);
        icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExplorerJourneyRequirement.StatusIcon)));
        row.AppendChild(icon);

        var text = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExplorerJourneyRequirement.Title)));
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetValue(TextBlock.FontSizeProperty, 10d);
        detail.SetValue(TextBlock.OpacityProperty, 0.72d);
        detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(ExplorerJourneyRequirement.Detail)));
        text.AppendChild(title);
        text.AppendChild(detail);
        row.AppendChild(text);
        return new DataTemplate { VisualTree = row };
    }

    private static Button? FindButtonForCommand(DependencyObject parent, ICommand expected)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button && ReferenceEquals(button.Command, expected)) return button;
            var nested = FindButtonForCommand(child, expected);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Panel? FindHostPanel(DependencyObject child)
    {
        DependencyObject? current = child;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is StackPanel { Orientation: Orientation.Vertical } panel) return panel;
        }
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;
}
