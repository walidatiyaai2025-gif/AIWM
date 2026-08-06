using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class SitesFirstJourneyExperience
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
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;
        if (Attached.TryGetValue(window, out _))
            return;
        if (window.DataContext is not MainWindowViewModel main)
            return;

        var state = new State(window, main);
        Attached.Add(window, state);
        state.Attach();
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private Border? _card;

        public void Attach()
        {
            main.PropertyChanged += OnMainPropertyChanged;
            main.Sites.PropertyChanged += OnSitesPropertyChanged;
            main.Sites.Sites.CollectionChanged += OnSitesCollectionChanged;
            main.Sites.Wizard.SiteSaved += OnSiteSaved;
            window.Closed += OnClosed;
            window.Dispatcher.BeginInvoke(new Action(InstallAndRefresh));
        }

        private void InstallAndRefresh()
        {
            _card ??= InstallCard(window, main);
            ApplyVisibility();
            main.Sites.RefreshFirstJourneyReadiness();
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                ApplyVisibility();
        }

        private void OnSitesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SitesViewModel.SelectedSite) or
                nameof(SitesViewModel.SelectedSiteDetails) or
                nameof(SitesViewModel.IsTestingConnection) or
                nameof(SitesViewModel.IsLoading))
            {
                main.Sites.RefreshFirstJourneyReadiness();
            }
        }

        private void OnSitesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => main.Sites.RefreshFirstJourneyReadiness();

        private void OnSiteSaved(object? sender, EventArgs e)
            => window.Dispatcher.BeginInvoke(new Action(main.Sites.RefreshFirstJourneyReadiness));

        private void ApplyVisibility()
        {
            if (_card is null)
                return;

            var visible = main.CurrentPage.Equals("Sites", StringComparison.OrdinalIgnoreCase);
            _card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            _card.IsHitTestVisible = visible;
            if (visible)
                main.Sites.RefreshFirstJourneyReadiness();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnMainPropertyChanged;
            main.Sites.PropertyChanged -= OnSitesPropertyChanged;
            main.Sites.Sites.CollectionChanged -= OnSitesCollectionChanged;
            main.Sites.Wizard.SiteSaved -= OnSiteSaved;
            window.Closed -= OnClosed;
        }
    }

    private static Border? InstallCard(MainWindow window, MainWindowViewModel main)
    {
        var addButton = FindButtonForCommand(window, main.Sites.AddSiteCommand);
        if (addButton is null)
            return null;

        var host = FindPageStackPanel(addButton);
        if (host is null)
            return null;

        var existing = host.Children.OfType<Border>()
            .FirstOrDefault(item => Equals(item.Tag, "SitesFirstJourneyGate"));
        if (existing is not null)
            return existing;

        var card = new Border
        {
            Tag = "SitesFirstJourneyGate",
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.LightGray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke)
        };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "STEP 1 · COMPLETE SITES SETUP",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Register, select and verify the WordPress site before synchronization.",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 4)
        });

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.DimGray)
        };
        status.SetBinding(TextBlock.TextProperty, new Binding("Sites.FirstJourneyStatus"));
        root.Children.Add(status);

        var requirements = new ItemsControl();
        requirements.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Sites.FirstJourneyRequirements"));
        requirements.ItemTemplate = BuildRequirementTemplate();
        root.Children.Add(requirements);

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(CreateActionButton(
            "Add site",
            new Binding("Sites.AddSiteCommand"),
            window,
            primary: true));
        actions.Children.Add(CreateActionButton(
            "Retest selected site",
            new Binding("Sites.RetestSelectedSiteCommand"),
            window));

        var continueButton = CreateActionButton(
            "Continue to WordPress Explorer",
            new Binding("NavigateCommand"),
            window,
            primary: true);
        continueButton.CommandParameter = "WordPress Explorer";
        continueButton.SetBinding(UIElement.IsEnabledProperty, new Binding("Sites.IsFirstJourneyReady"));
        actions.Children.Add(continueButton);
        root.Children.Add(actions);

        card.Child = root;
        host.Children.Insert(0, card);
        return card;
    }

    private static DataTemplate BuildRequirementTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5));

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetValue(FrameworkElement.WidthProperty, 24d);
        icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(SiteJourneyRequirement.StatusIcon)));
        icon.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SiteJourneyRequirement.StatusBrush)));
        panel.AppendChild(icon);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(SiteJourneyRequirement.Title)));
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SiteJourneyRequirement.Description)));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private static Button CreateActionButton(string text, Binding commandBinding, FrameworkElement owner, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 9, 7),
            Padding = new Thickness(13, 8, 13, 8),
            MinWidth = 145
        };
        var styleKey = primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle";
        if (owner.TryFindResource(styleKey) is Style style)
            button.Style = style;
        button.SetBinding(Button.CommandProperty, commandBinding);
        return button;
    }

    private static Button? FindButtonForCommand(DependencyObject parent, ICommand expectedCommand)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && ReferenceEquals(button.Command, expectedCommand))
                return button;
            var nested = FindButtonForCommand(child, expectedCommand);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static StackPanel? FindPageStackPanel(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        StackPanel? candidate = null;
        while (current is not null)
        {
            if (current is StackPanel stack && stack.Orientation == Orientation.Vertical)
            {
                candidate = stack;
                if (stack.Children.Count >= 2)
                    return stack;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return candidate;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;
}
