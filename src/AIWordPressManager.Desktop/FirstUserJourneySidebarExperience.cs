using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<FirstJourneyPage> FirstJourneySidebarPages { get; } = [];

    private string _firstJourneySidebarSummary = "Start with Dashboard, then complete each required page in order.";
    public string FirstJourneySidebarSummary
    {
        get => _firstJourneySidebarSummary;
        private set => SetProperty(ref _firstJourneySidebarSummary, value);
    }

    internal void RefreshFirstJourneySidebar()
    {
        var completionByTarget = CompleteJourneySteps
            .GroupBy(step => step.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.All(step => step.IsCompleted),
                StringComparer.OrdinalIgnoreCase);

        var currentTarget = CompleteJourneySteps.FirstOrDefault(step => step.IsCurrent)?.Target ?? "Sites";
        var definitions = new[]
        {
            new FirstJourneyDefinition("0", "Dashboard", "Journey overview and next required action."),
            new FirstJourneyDefinition("1", "Sites", "Register, test and select the WordPress site."),
            new FirstJourneyDefinition("2", "WordPress Explorer", "Synchronize and verify the local WordPress snapshot."),
            new FirstJourneyDefinition("3", "SEO Audit", "Build the first SEO and technical baseline."),
            new FirstJourneyDefinition("4", "Suggested Changes", "Review recommendations and preview proposed values."),
            new FirstJourneyDefinition("5", "Approval Queue", "Approve only the safe changes for execution."),
            new FirstJourneyDefinition("6", "Execution Center", "Back up, execute and preserve the audit receipt."),
            new FirstJourneyDefinition("7", "Evidence Center", "Verify before/after values and rollback evidence.")
        };

        FirstJourneySidebarPages.Clear();
        foreach (var definition in definitions)
        {
            var isDashboard = definition.Target.Equals("Dashboard", StringComparison.OrdinalIgnoreCase);
            var isCompleted = isDashboard
                ? CompleteJourneySteps.Count > 0
                : completionByTarget.TryGetValue(definition.Target, out var completed) && completed;
            var isCurrent = isDashboard
                ? CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase)
                : definition.Target.Equals(currentTarget, StringComparison.OrdinalIgnoreCase);

            FirstJourneySidebarPages.Add(new FirstJourneyPage(
                definition.Number,
                definition.Target,
                definition.Description,
                isCompleted,
                isCurrent,
                NavigateCommand));
        }

        var completedPages = FirstJourneySidebarPages.Count(page => page.IsCompleted);
        FirstJourneySidebarSummary = completedPages >= FirstJourneySidebarPages.Count
            ? "First journey completed and verified."
            : $"{completedPages} of {FirstJourneySidebarPages.Count} pages ready.";
    }

    private sealed record FirstJourneyDefinition(string Number, string Target, string Description);
}

public sealed record FirstJourneyPage(
    string Number,
    string Target,
    string Description,
    bool IsCompleted,
    bool IsCurrent,
    ICommand NavigateCommand)
{
    public string DisplayTitle => $"{Number}. {Target}";
    public string StatusIcon => IsCompleted ? "✓" : IsCurrent ? "▶" : "○";
    public Brush StatusBrush => IsCompleted ? Brushes.SeaGreen : IsCurrent ? Brushes.DodgerBlue : Brushes.SlateGray;
}

namespace AIWordPressManager.Desktop;

internal static class FirstUserJourneySidebarBootstrap
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
        private Border? _journeyPanel;

        public void Attach()
        {
            main.CompleteJourneySteps.CollectionChanged += OnJourneyStepsChanged;
            main.PropertyChanged += OnMainPropertyChanged;
            window.Closed += OnClosed;
            window.Dispatcher.BeginInvoke(new Action(InstallAndRefresh));
        }

        private void InstallAndRefresh()
        {
            _journeyPanel ??= InstallJourneyPanel(window);
            main.RefreshFirstJourneySidebar();
        }

        private void OnJourneyStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => main.RefreshFirstJourneySidebar();

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or
                nameof(MainWindowViewModel.CurrentJourneyTarget) or
                nameof(MainWindowViewModel.CompleteJourneyPercent))
            {
                main.RefreshFirstJourneySidebar();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.CompleteJourneySteps.CollectionChanged -= OnJourneyStepsChanged;
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Closed -= OnClosed;
        }
    }

    private static Border? InstallJourneyPanel(MainWindow window)
    {
        var dashboardButton = FindNavigationButton(window, "Dashboard");
        var sitesButton = FindNavigationButton(window, "Sites");
        if (dashboardButton is null || sitesButton is null)
            return null;

        var sidebar = FindSharedVerticalPanel(dashboardButton, sitesButton);
        if (sidebar is null)
            return null;

        var existing = sidebar.Children.OfType<Border>()
            .FirstOrDefault(item => Equals(item.Tag, "FirstUserJourneySidebar"));
        if (existing is not null)
            return existing;

        var panel = new Border
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
        root.Children.Add(new TextBlock
        {
            Text = "FIRST USER JOURNEY",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
        });

        var summary = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 8),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.Gray)
        };
        summary.SetBinding(TextBlock.TextProperty, new Binding("FirstJourneySidebarSummary"));
        root.Children.Add(summary);

        var items = new ItemsControl();
        items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("FirstJourneySidebarPages"));
        items.ItemTemplate = BuildPageTemplate(window);
        root.Children.Add(items);
        panel.Child = root;

        sidebar.Children.Insert(0, panel);
        return panel;
    }

    private static DataTemplate BuildPageTemplate(FrameworkElement resourceOwner)
    {
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(Button.MarginProperty, new Thickness(0, 0, 0, 4));
        button.SetValue(Button.PaddingProperty, new Thickness(7, 6, 7, 6));
        button.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        button.SetBinding(Button.CommandProperty, new Binding(nameof(FirstJourneyPage.NavigateCommand)));
        button.SetBinding(Button.CommandParameterProperty, new Binding(nameof(FirstJourneyPage.Target)));
        button.SetBinding(Button.ToolTipProperty, new Binding(nameof(FirstJourneyPage.Description)));

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.AppendChild(CreatePageContentFactory());
        button.AppendChild(grid);
        return new DataTemplate { VisualTree = button };
    }

    private static FrameworkElementFactory CreatePageContentFactory()
    {
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
        return panel;
    }

    private static Button? FindNavigationButton(DependencyObject parent, string target)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button &&
                string.Equals(button.CommandParameter?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }

            var nested = FindNavigationButton(child, target);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static Panel? FindSharedVerticalPanel(DependencyObject first, DependencyObject second)
    {
        var firstParents = new HashSet<DependencyObject>();
        var current = VisualTreeHelper.GetParent(first);
        while (current is not null)
        {
            firstParents.Add(current);
            current = VisualTreeHelper.GetParent(current);
        }

        current = VisualTreeHelper.GetParent(second);
        while (current is not null)
        {
            if (current is StackPanel { Orientation: Orientation.Vertical } stack && firstParents.Contains(current))
                return stack;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;
}
