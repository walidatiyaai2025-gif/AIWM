using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class FirstUserJourneySidebarBootstrap
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
        if (Attached.TryGetValue(window, out _) || window.DataContext is not MainWindowViewModel main) return;
        var state = new State(window, main);
        Attached.Add(window, state);
        state.Attach();
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        public void Attach()
        {
            main.CompleteJourneySteps.CollectionChanged += OnJourneyChanged;
            main.PropertyChanged += OnMainChanged;
            window.Closed += OnClosed;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                InstallJourneyPanel(window);
                main.RefreshFirstJourneySidebar();
            }));
        }

        private void OnJourneyChanged(object? sender, NotifyCollectionChangedEventArgs e) => main.RefreshFirstJourneySidebar();

        private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or nameof(MainWindowViewModel.CurrentJourneyTarget) or nameof(MainWindowViewModel.CompleteJourneyPercent))
                main.RefreshFirstJourneySidebar();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.CompleteJourneySteps.CollectionChanged -= OnJourneyChanged;
            main.PropertyChanged -= OnMainChanged;
            window.Closed -= OnClosed;
        }
    }

    private static void InstallJourneyPanel(MainWindow window)
    {
        var dashboard = FindNavigationButton(window, "Dashboard");
        var sites = FindNavigationButton(window, "Sites");
        if (dashboard is null || sites is null) return;
        var sidebar = FindSharedVerticalPanel(dashboard, sites);
        if (sidebar is null || sidebar.Children.OfType<Border>().Any(x => Equals(x.Tag, "FirstUserJourneySidebar"))) return;

        var border = new Border
        {
            Tag = "FirstUserJourneySidebar",
            Margin = new Thickness(8, 8, 8, 12),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.DimGray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.Transparent)
        };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "FIRST USER JOURNEY", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue) });
        var summary = new TextBlock { Margin = new Thickness(0, 4, 0, 8), FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.Gray) };
        summary.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneySidebarSummary"));
        root.Children.Add(summary);
        var items = new ItemsControl { ItemTemplate = BuildTemplate() };
        items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FirstJourneySidebarPages"));
        root.Children.Add(items);
        border.Child = root;
        sidebar.Children.Insert(0, border);
    }

    private static DataTemplate BuildTemplate()
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(Button.MarginProperty, new Thickness(0, 0, 0, 4));
        button.SetValue(Button.PaddingProperty, new Thickness(7, 6, 7, 6));
        button.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        button.SetBinding(Button.CommandProperty, new Binding(nameof(FirstJourneyPage.NavigateCommand)));
        button.SetBinding(Button.CommandParameterProperty, new Binding(nameof(FirstJourneyPage.Target)));
        button.SetBinding(Button.ToolTipProperty, new Binding(nameof(FirstJourneyPage.Description)));
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(TextBlock.WidthProperty, 22d);
        icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(FirstJourneyPage.StatusIcon)));
        icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(FirstJourneyPage.StatusBrush)));
        panel.AppendChild(icon);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetValue(TextBlock.FontSizeProperty, 10.5d);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(FirstJourneyPage.DisplayTitle)));
        panel.AppendChild(title);
        button.AppendChild(panel);
        return new DataTemplate { VisualTree = button };
    }

    private static Button? FindNavigationButton(DependencyObject parent, string target)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button && string.Equals(button.CommandParameter?.ToString(), target, StringComparison.OrdinalIgnoreCase)) return button;
            var nested = FindNavigationButton(child, target);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Panel? FindSharedVerticalPanel(DependencyObject first, DependencyObject second)
    {
        var parents = new HashSet<DependencyObject>();
        for (var current = VisualTreeHelper.GetParent(first); current is not null; current = VisualTreeHelper.GetParent(current)) parents.Add(current);
        for (var current = VisualTreeHelper.GetParent(second); current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is StackPanel { Orientation: Orientation.Vertical } stack && parents.Contains(current)) return stack;
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;
}
