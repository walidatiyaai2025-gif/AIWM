using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class WorkPageFocusExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly HashSet<string> NonWorkPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard",
        "Sites",
        "Help",
        "Settings",
        "Notification Center"
    };

    private static readonly string[] AuxiliaryPanelTags =
    [
        "PriorityResolutionPanel",
        "ReviewWorkbenchesPanel",
        "ContentQualityBatchPanel",
        "QuickFixJourneyPanel",
        "MediaAnalysisPanel",
        "AiCopilotInboxPanel"
    ];

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

        var state = new State(window, root, main);
        Attached.Add(window, state);

        var bar = BuildPrimaryActionBar(main);
        state.ActionBar = bar;
        Grid.SetRow(bar, 2);
        Panel.SetZIndex(bar, 120);

        foreach (var child in root.Children.OfType<FrameworkElement>()
                     .Where(x => Grid.GetRow(x) == 2)
                     .ToArray())
        {
            child.Visibility = Visibility.Collapsed;
            child.IsHitTestVisible = false;
        }

        root.Children.Add(bar);

        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)
                or nameof(MainWindowViewModel.CurrentJourneyActionLabel))
            {
                Apply(state);
            }
        };

        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) => Apply(state);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Apply(state);
    }

    private static Border BuildPrimaryActionBar(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Height = 36,
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 0),
            Tag = "PrimaryWorkActionBar"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = grid;

        var title = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black),
            Margin = new Thickness(0, 0, 12, 0)
        };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.PageTitle)));
        grid.Children.Add(title);

        var context = new StackPanel
        {
            Grid.ColumnProperty = { },
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(context, 1);
        grid.Children.Add(context);

        var site = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal),
            Margin = new Thickness(0, 0, 10, 0)
        };
        site.SetBinding(TextBlock.TextProperty,
            new Binding(nameof(MainWindowViewModel.DashboardSelectedSite)) { StringFormat = "Site: {0}" });
        context.Children.Add(site);

        context.Children.Add(new Border
        {
            Width = 1,
            Height = 14,
            Background = Brush("BorderBrush", Brushes.LightGray),
            Margin = new Thickness(0, 0, 10, 0)
        });

        var step = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        step.SetBinding(TextBlock.TextProperty,
            new Binding(nameof(MainWindowViewModel.CurrentJourneyStepTitle)) { StringFormat = "Next: {0}" });
        context.Children.Add(step);

        var action = new Button
        {
            MinWidth = 130,
            Height = 26,
            Padding = new Thickness(12, 2, 12, 2),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Command = main.ContinueJourneyCommand,
            Tag = "PrimaryJourneyActionButton"
        };
        action.SetBinding(ContentControl.ContentProperty,
            new Binding(nameof(MainWindowViewModel.CurrentJourneyActionLabel)));
        action.SetBinding(UIElement.IsEnabledProperty,
            new Binding(nameof(MainWindowViewModel.IsOperationRunning))
            {
                Converter = new InverseBooleanConverter()
            });
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);

        return shell;
    }

    private static void Apply(State state)
    {
        var isWorkPage = !NonWorkPages.Contains(state.Main.CurrentPage);
        state.ActionBar.Visibility = isWorkPage ? Visibility.Visible : Visibility.Collapsed;
        state.ActionBar.IsHitTestVisible = isWorkPage;

        if (!isWorkPage) return;

        foreach (var tag in AuxiliaryPanelTags)
        {
            var panel = FindByTag<FrameworkElement>(state.Root, tag);
            if (panel is null) continue;
            panel.Visibility = Visibility.Collapsed;
            panel.IsHitTestVisible = false;
        }

        foreach (var surface in Enumerate<FrameworkElement>(state.Root))
        {
            if (ReferenceEquals(surface, state.ActionBar)) continue;
            if (surface.Tag?.ToString() is "FloatingWorkspaceScrim" or "FloatingWorkspaceLauncher") continue;

            if (surface.Tag?.ToString() is string tag &&
                (tag.Contains("LiveOperations", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("ApprovedChanges", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("AiCopilotInbox", StringComparison.OrdinalIgnoreCase)))
            {
                surface.Visibility = Visibility.Collapsed;
                surface.IsHitTestVisible = false;
            }
        }
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindByTag<T>(child, tag);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed class State(MainWindow window, Grid root, MainWindowViewModel main)
    {
        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
        public Border ActionBar { get; set; } = null!;
    }

    private sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool flag && !flag;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }
}
