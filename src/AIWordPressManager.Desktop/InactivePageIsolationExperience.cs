using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Isolates inactive page surfaces so collapsed pages cannot retain keyboard focus
/// or receive input after navigation. The behavior is event driven.
/// </summary>
internal static class InactivePageIsolationExperience
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
        if (e.OriginalSource is not Grid contentHost ||
            !string.Equals(contentHost.Tag?.ToString(), "DockedWorkspaceContent", StringComparison.Ordinal))
            return;

        if (Window.GetWindow(contentHost) is not MainWindow window) return;

        if (!Attached.TryGetValue(window, out var state))
        {
            state = new State(window);
            Attached.Add(window, state);
        }

        state.Bind(contentHost);
    }

    private sealed class State(MainWindow window)
    {
        private readonly List<FrameworkElement> _pages = [];
        private Grid? _contentHost;
        private bool _focusQueued;

        public void Bind(Grid contentHost)
        {
            if (ReferenceEquals(_contentHost, contentHost)) return;
            DetachPages();

            _contentHost = contentHost;
            foreach (var page in contentHost.Children.OfType<FrameworkElement>())
            {
                _pages.Add(page);
                page.IsVisibleChanged += OnPageVisibilityChanged;
                ApplyIsolation(page);
            }

            window.Closed -= OnClosed;
            window.Closed += OnClosed;
            QueueFocusVisiblePage();
        }

        private void OnPageVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not FrameworkElement page) return;
            ApplyIsolation(page);

            if (page.IsVisible)
                QueueFocusVisiblePage();
            else if (IsKeyboardFocusWithin(page))
                Keyboard.ClearFocus();
        }

        private static void ApplyIsolation(FrameworkElement page)
        {
            var active = page.IsVisible && page.Visibility == Visibility.Visible;
            page.IsHitTestVisible = active;
            page.Focusable = active;
        }

        private void QueueFocusVisiblePage()
        {
            if (_focusQueued || window.Dispatcher.HasShutdownStarted) return;
            _focusQueued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                _focusQueued = false;
                if (!window.IsActive) return;

                var visible = _pages.FirstOrDefault(page =>
                    page.IsVisible && page.Visibility == Visibility.Visible && page.IsEnabled);
                if (visible is null) return;

                visible.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
        }

        private static bool IsKeyboardFocusWithin(DependencyObject root)
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused is not null)
            {
                if (ReferenceEquals(focused, root)) return true;
                focused = focused is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(focused)
                    : LogicalTreeHelper.GetParent(focused);
            }
            return false;
        }

        private void DetachPages()
        {
            foreach (var page in _pages)
                page.IsVisibleChanged -= OnPageVisibilityChanged;
            _pages.Clear();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            window.Closed -= OnClosed;
            DetachPages();
            _contentHost = null;
        }
    }
}
