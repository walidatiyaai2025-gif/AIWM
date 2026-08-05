using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Adds lightweight counters to the existing docked sidebar and selects the most
/// relevant section when the current page changes. The sidebar is never opened
/// automatically, so the main workspace is not resized without user intent.
/// </summary>
internal static class DockedSidebarContextExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnElementLoaded),
            true);
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Border sidebar ||
            !string.Equals(sidebar.Tag?.ToString(), "DockedRightSidebar", StringComparison.OrdinalIgnoreCase))
            return;

        if (Window.GetWindow(sidebar) is not MainWindow window ||
            window.DataContext is not MainWindowViewModel main)
            return;

        if (!Attached.TryGetValue(window, out var state))
        {
            state = new State(window, main);
            Attached.Add(window, state);
            state.Attach();
        }

        state.BindSidebar(sidebar);
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BadgeControls> _badges = new(StringComparer.OrdinalIgnoreCase);
        private Grid? _host;
        private Border? _sidebar;
        private string? _selectedLabel;
        private bool _refreshQueued;

        public void Attach()
        {
            main.PropertyChanged += OnMainPropertyChanged;
            main.SuggestedChanges.PropertyChanged += OnRelatedPropertyChanged;
            main.ExecutionCenter.PropertyChanged += OnRelatedPropertyChanged;
            main.SuggestedChanges.Items.CollectionChanged += OnCollectionChanged;
            main.ExecutionCenter.Items.CollectionChanged += OnCollectionChanged;
            main.Operations.History.CollectionChanged += OnCollectionChanged;
            main.Operations.Operations.CollectionChanged += OnCollectionChanged;
            window.Closed += OnClosed;
        }

        public void BindSidebar(Border sidebar)
        {
            _sidebar = sidebar;
            _host = FindByTag<Grid>(window, "DockedWorkspaceHost");
            _buttons.Clear();
            _badges.Clear();

            foreach (var button in Enumerate<Button>(sidebar))
            {
                var label = button.ToolTip?.ToString();
                if (label is not ("Journey" or "AI Copilot" or "Operations" or "Notifications" or "Quick Fix"))
                    continue;

                _buttons[label] = button;
                var badge = EnsureBadge(button);
                if (badge is not null)
                    _badges[label] = badge;
            }

            QueueRefresh();
            SelectForCurrentPage();
        }

        private static BadgeControls? EnsureBadge(Button button)
        {
            if (button.Content is not StackPanel panel) return null;

            var existing = panel.Children.OfType<Border>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "SidebarContextBadge", StringComparison.Ordinal));
            if (existing?.Child is TextBlock existingText)
                return new BadgeControls(existing, existingText);

            var text = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };

            var badge = new Border
            {
                Tag = "SidebarContextBadge",
                MinWidth = 20,
                Height = 18,
                Margin = new Thickness(5, 0, 2, 0),
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                CornerRadius = new CornerRadius(9),
                Background = ResourceBrush("PrimaryBrush", Brushes.Teal),
                Visibility = Visibility.Collapsed,
                Child = text
            };
            panel.Children.Add(badge);
            return new BadgeControls(badge, text);
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                SelectForCurrentPage();

            if (e.PropertyName is null or
                nameof(MainWindowViewModel.CurrentPage) or
                nameof(MainWindowViewModel.DashboardJourneyProgress) or
                nameof(MainWindowViewModel.DashboardAiSuggestions) or
                nameof(MainWindowViewModel.DashboardRunningJobs) or
                nameof(MainWindowViewModel.DashboardFailedJobs) or
                nameof(MainWindowViewModel.IsOperationRunning))
                QueueRefresh();
        }

        private void OnRelatedPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

        private void QueueRefresh()
        {
            if (_refreshQueued || window.Dispatcher.HasShutdownStarted) return;
            _refreshQueued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                RefreshBadges();
            }));
        }

        private void RefreshBadges()
        {
            SetBadge("Journey", $"{Math.Clamp(main.DashboardJourneyProgress, 0, 100)}%",
                main.DashboardJourneyProgress > 0);

            SetBadge("AI Copilot", Compact(main.DashboardAiSuggestions), main.DashboardAiSuggestions > 0);

            var running = Math.Max(main.DashboardRunningJobs, main.IsOperationRunning ? 1 : 0);
            SetBadge("Operations", Compact(running), running > 0);

            var notificationCount = main.Operations.History.Count(item =>
                item.State is "Failed" or "Completed" or "Cancelled");
            SetBadge("Notifications", Compact(notificationCount), notificationCount > 0);

            var quickFixCount = main.SuggestedChanges.PendingCount +
                                main.SuggestedChanges.ApprovedCount +
                                main.ExecutionCenter.ReadyCount;
            SetBadge("Quick Fix", Compact(quickFixCount), quickFixCount > 0);
        }

        private void SetBadge(string label, string text, bool visible)
        {
            if (!_badges.TryGetValue(label, out var badge)) return;
            badge.Text.Text = text;
            badge.Container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectForCurrentPage()
        {
            if (!IsExpanded()) return;

            var label = MapPage(main.CurrentPage);
            if (label is null || string.Equals(label, _selectedLabel, StringComparison.OrdinalIgnoreCase)) return;
            if (!_buttons.TryGetValue(label, out var button) || !button.IsEnabled) return;

            _selectedLabel = label;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        private bool IsExpanded() =>
            _host?.ColumnDefinitions.Count >= 2 && _host.ColumnDefinitions[1].Width.Value > 44.5;

        private static string? MapPage(string? page)
        {
            if (string.IsNullOrWhiteSpace(page)) return null;

            if (page.Contains("Notification", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Timeline", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Evidence", StringComparison.OrdinalIgnoreCase))
                return "Notifications";

            if (page.Contains("Operation", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Job", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Execution", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Scheduler", StringComparison.OrdinalIgnoreCase))
                return "Operations";

            if (page.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Article Generator", StringComparison.OrdinalIgnoreCase))
                return "AI Copilot";

            if (page.Contains("Suggested", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Approval", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Quick", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("SEO Audit", StringComparison.OrdinalIgnoreCase))
                return "Quick Fix";

            if (page.Equals("Dashboard", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Site", StringComparison.OrdinalIgnoreCase) ||
                page.Contains("Explorer", StringComparison.OrdinalIgnoreCase))
                return "Journey";

            return null;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnMainPropertyChanged;
            main.SuggestedChanges.PropertyChanged -= OnRelatedPropertyChanged;
            main.ExecutionCenter.PropertyChanged -= OnRelatedPropertyChanged;
            main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            main.ExecutionCenter.Items.CollectionChanged -= OnCollectionChanged;
            main.Operations.History.CollectionChanged -= OnCollectionChanged;
            main.Operations.Operations.CollectionChanged -= OnCollectionChanged;
            window.Closed -= OnClosed;

            _buttons.Clear();
            _badges.Clear();
            _sidebar = null;
            _host = null;
        }
    }

    private sealed record BadgeControls(Border Container, TextBlock Text);

    private static string Compact(int value) => value > 99 ? "99+" : value.ToString();

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && string.Equals(match.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            return match;

        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var found = FindByTag<T>(child, tag);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T current) yield return current;
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }
}
