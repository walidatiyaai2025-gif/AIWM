using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Owns all behavior around the docked workspace sidebar: cached controls,
/// keyboard shortcuts, responsive width, context badges and page-aware selection.
/// One controller avoids multiple layers competing over the same UI state.
/// </summary>
internal static class DockedSidebarControllerExperience
{
    private const double CollapsedWidth = 44;
    private const double CompactWidth = 270;
    private const double FullWidth = 320;
    private const double AutoCollapseThreshold = 1150;
    private const double CompactThreshold = 1350;

    private static readonly string[] SectionLabels =
    [
        "Journey",
        "AI Copilot",
        "Operations",
        "Notifications",
        "Quick Fix"
    ];

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

        state.Bind(sidebar);
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        private readonly Dictionary<string, Button> _sectionButtons =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BadgeControls> _badges =
            new(StringComparer.OrdinalIgnoreCase);

        private Grid? _host;
        private Border? _sidebar;
        private Button? _toggle;
        private string? _selectedLabel;
        private bool _refreshQueued;
        private bool _applyingResponsiveState;
        private bool _autoCollapsed;
        private bool _userPreferredExpanded;
        private bool _bound;

        public void Attach()
        {
            window.PreviewKeyDown += OnPreviewKeyDown;
            window.SizeChanged += OnSizeChanged;
            window.StateChanged += OnWindowStateChanged;
            window.Closed += OnClosed;

            main.PropertyChanged += OnMainPropertyChanged;
            main.SuggestedChanges.PropertyChanged += OnRelatedPropertyChanged;
            main.ExecutionCenter.PropertyChanged += OnRelatedPropertyChanged;
            main.SuggestedChanges.Items.CollectionChanged += OnCollectionChanged;
            main.ExecutionCenter.Items.CollectionChanged += OnCollectionChanged;
            main.Operations.History.CollectionChanged += OnCollectionChanged;
            main.Operations.Operations.CollectionChanged += OnCollectionChanged;
        }

        public void Bind(Border sidebar)
        {
            _sidebar = sidebar;
            _host = FindByTag<Grid>(window, "DockedWorkspaceHost");
            if (_host is null || _host.ColumnDefinitions.Count < 2) return;

            _sectionButtons.Clear();
            _badges.Clear();
            _toggle = null;

            foreach (var button in Enumerate<Button>(sidebar))
            {
                var label = button.ToolTip?.ToString();
                if (label is not null && SectionLabels.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    _sectionButtons[label] = button;
                    var badge = EnsureBadge(button);
                    if (badge is not null)
                        _badges[label] = badge;
                }

                if (button.Content?.ToString() is "☰" or "✕")
                    _toggle = button;
            }

            if (_toggle is null || _sectionButtons.Count == 0) return;

            if (_bound)
                _toggle.Click -= OnToggleClicked;

            _toggle.Click += OnToggleClicked;
            _bound = true;
            _userPreferredExpanded = IsExpanded();

            QueueRefresh();
            ApplyResponsiveWidth();
            SelectForCurrentPage();
        }

        private static BadgeControls? EnsureBadge(Button button)
        {
            if (button.Content is not StackPanel panel) return null;

            var existing = panel.Children.OfType<Border>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    "SidebarContextBadge",
                    StringComparison.Ordinal));
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

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && TryClose())
            {
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift))
                return;

            var label = e.Key switch
            {
                Key.N => "Notifications",
                Key.J => "Journey",
                Key.O => "Operations",
                Key.A => "AI Copilot",
                Key.Q => "Quick Fix",
                _ => null
            };
            if (label is null || !_sectionButtons.TryGetValue(label, out var button) || !button.IsEnabled)
                return;

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            e.Handled = true;
        }

        private bool TryClose()
        {
            if (!IsExpanded() || _toggle is null || !_toggle.IsEnabled) return false;
            _toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, _toggle));
            return true;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            if (_applyingResponsiveState) return;

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    _userPreferredExpanded = IsExpanded();
                    _autoCollapsed = false;
                    ApplyResponsiveWidth();
                    SelectForCurrentPage();
                }));
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveWidth();
        private void OnWindowStateChanged(object? sender, EventArgs e) => ApplyResponsiveWidth();

        private void ApplyResponsiveWidth()
        {
            if (_applyingResponsiveState || _host is null || _toggle is null ||
                _host.ColumnDefinitions.Count < 2 || window.Dispatcher.HasShutdownStarted)
                return;

            _applyingResponsiveState = true;
            try
            {
                var availableWidth = window.WindowState == WindowState.Minimized ? 0 : window.ActualWidth;

                if (availableWidth < AutoCollapseThreshold)
                {
                    if (IsExpanded())
                    {
                        _userPreferredExpanded = true;
                        SetExpanded(false);
                    }

                    _autoCollapsed = true;
                    SetWidth(CollapsedWidth);
                    return;
                }

                if (_autoCollapsed)
                {
                    _autoCollapsed = false;
                    if (_userPreferredExpanded && !IsExpanded())
                        SetExpanded(true);
                }

                if (!IsExpanded())
                {
                    SetWidth(CollapsedWidth);
                    return;
                }

                SetWidth(availableWidth < CompactThreshold ? CompactWidth : FullWidth);
            }
            finally
            {
                _applyingResponsiveState = false;
            }
        }

        private void SetExpanded(bool expanded)
        {
            if (_toggle is null || IsExpanded() == expanded) return;
            _toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, _toggle));
        }

        private void SetWidth(double width)
        {
            if (_host is null || _host.ColumnDefinitions.Count < 2) return;
            _host.ColumnDefinitions[1].Width = new GridLength(width);
        }

        private bool IsExpanded() =>
            _host?.ColumnDefinitions.Count >= 2 &&
            _host.ColumnDefinitions[1].Width.Value > CollapsedWidth + 0.5;

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

        private void SetBadge(string label, string value, bool visible)
        {
            if (!_badges.TryGetValue(label, out var badge)) return;
            badge.Text.Text = value;
            badge.Container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectForCurrentPage()
        {
            if (!IsExpanded()) return;

            var label = MapPage(main.CurrentPage);
            if (label is null || string.Equals(label, _selectedLabel, StringComparison.OrdinalIgnoreCase))
                return;
            if (!_sectionButtons.TryGetValue(label, out var button) || !button.IsEnabled)
                return;

            _selectedLabel = label;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

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
            window.PreviewKeyDown -= OnPreviewKeyDown;
            window.SizeChanged -= OnSizeChanged;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnClosed;

            main.PropertyChanged -= OnMainPropertyChanged;
            main.SuggestedChanges.PropertyChanged -= OnRelatedPropertyChanged;
            main.ExecutionCenter.PropertyChanged -= OnRelatedPropertyChanged;
            main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            main.ExecutionCenter.Items.CollectionChanged -= OnCollectionChanged;
            main.Operations.History.CollectionChanged -= OnCollectionChanged;
            main.Operations.Operations.CollectionChanged -= OnCollectionChanged;

            if (_toggle is not null)
                _toggle.Click -= OnToggleClicked;

            _sectionButtons.Clear();
            _badges.Clear();
            _host = null;
            _sidebar = null;
            _toggle = null;
            _bound = false;
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
            foreach (var nested in Enumerate<T>(child))
                yield return nested;
        }
    }
}
