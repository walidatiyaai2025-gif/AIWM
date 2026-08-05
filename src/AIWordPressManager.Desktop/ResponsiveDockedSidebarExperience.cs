using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the docked workspace sidebar from squeezing the active page on smaller windows.
/// The behavior is event driven and preserves the user's expanded preference.
/// </summary>
internal static class ResponsiveDockedSidebarExperience
{
    private const double CollapsedWidth = 44;
    private const double CompactWidth = 270;
    private const double FullWidth = 320;
    private const double AutoCollapseThreshold = 1150;
    private const double CompactThreshold = 1350;

    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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

        var state = new State(window);
        Attached.Add(window, state);

        window.SizeChanged += state.OnSizeChanged;
        window.StateChanged += state.OnWindowStateChanged;
        window.Closed += state.OnClosed;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(state.ResolveAndApply));
    }

    private sealed class State(MainWindow window)
    {
        private Grid? _host;
        private Border? _sidebar;
        private Button? _toggle;
        private bool _userPreferredExpanded;
        private bool _autoCollapsed;
        private bool _applying;

        public void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveWidth();

        public void OnWindowStateChanged(object? sender, EventArgs e) => ApplyResponsiveWidth();

        public void ResolveAndApply()
        {
            if (!ResolveControls()) return;

            _userPreferredExpanded = IsExpanded();
            _toggle!.Click -= OnToggleClicked;
            _toggle.Click += OnToggleClicked;
            ApplyResponsiveWidth();
        }

        private bool ResolveControls()
        {
            _host ??= FindByTag<Grid>(window, "DockedWorkspaceHost");
            _sidebar ??= FindByTag<Border>(window, "DockedRightSidebar");

            if (_host is null || _sidebar is null || _host.ColumnDefinitions.Count < 2)
            {
                _host = null;
                _sidebar = null;
                _toggle = null;
                return false;
            }

            _toggle ??= Enumerate<Button>(_sidebar)
                .FirstOrDefault(button => button.Content?.ToString() is "☰" or "✕");

            return _toggle is not null;
        }

        private void OnToggleClicked(object sender, RoutedEventArgs e)
        {
            if (_applying || !ResolveControls()) return;

            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    if (!ResolveControls()) return;
                    _userPreferredExpanded = IsExpanded();
                    _autoCollapsed = false;
                    ApplyResponsiveWidth();
                }));
        }

        private void ApplyResponsiveWidth()
        {
            if (_applying || window.Dispatcher.HasShutdownStarted) return;
            if (!ResolveControls())
            {
                window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(ResolveAndApply));
                return;
            }

            _applying = true;
            try
            {
                var availableWidth = window.WindowState == WindowState.Minimized
                    ? 0
                    : window.ActualWidth;

                if (availableWidth < AutoCollapseThreshold)
                {
                    if (IsExpanded())
                    {
                        _userPreferredExpanded = true;
                        SetExpanded(false);
                    }

                    _autoCollapsed = true;
                    SetColumnWidth(CollapsedWidth);
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
                    SetColumnWidth(CollapsedWidth);
                    return;
                }

                SetColumnWidth(availableWidth < CompactThreshold ? CompactWidth : FullWidth);
            }
            finally
            {
                _applying = false;
            }
        }

        private bool IsExpanded() =>
            _host is not null &&
            _host.ColumnDefinitions.Count > 1 &&
            _host.ColumnDefinitions[1].Width.Value > CollapsedWidth + 0.5;

        private void SetExpanded(bool expanded)
        {
            if (_toggle is null || IsExpanded() == expanded) return;
            _toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, _toggle));
        }

        private void SetColumnWidth(double width)
        {
            if (_host is null || _host.ColumnDefinitions.Count < 2) return;
            _host.ColumnDefinitions[1].Width = new GridLength(width);
        }

        public void OnClosed(object? sender, EventArgs e)
        {
            window.SizeChanged -= OnSizeChanged;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnClosed;

            if (_toggle is not null)
                _toggle.Click -= OnToggleClicked;

            _toggle = null;
            _sidebar = null;
            _host = null;
        }
    }

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
