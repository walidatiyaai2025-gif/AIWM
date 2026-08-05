using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Restricts the legacy application-level live dashboard timer to pages that actually
/// display live job/dashboard information. The timer is stopped while the application
/// is inactive and while the user is working in non-live pages such as editors,
/// settings, reports, backups, and SEO workspaces.
/// </summary>
internal static class PageScopedGlobalTimerExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly HashSet<string> LivePages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard",
        "Jobs",
        "Notification Center",
        "Activity Timeline",
        "Execution Center",
        "Operations Center"
    };

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
        if (window.DataContext is not MainWindowViewModel main) return;

        var timer = ResolveLiveTimer(main);
        if (timer is null) return;

        var state = new State(window, main, timer);
        Attached.Add(window, state);
        state.Attach();
    }

    private static DispatcherTimer? ResolveLiveTimer(MainWindowViewModel main)
    {
        var field = typeof(MainWindowViewModel).GetField(
            "_liveDashboardTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return field?.GetValue(main) as DispatcherTimer;
    }

    private sealed class State(MainWindow window, MainWindowViewModel main, DispatcherTimer timer)
    {
        private bool _windowActive = window.IsActive;

        public void Attach()
        {
            main.PropertyChanged += OnMainPropertyChanged;
            window.Activated += OnActivated;
            window.Deactivated += OnDeactivated;
            window.StateChanged += OnWindowStateChanged;
            window.Closed += OnClosed;

            ApplyTimerState();
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                ApplyTimerState();
        }

        private void OnActivated(object? sender, EventArgs e)
        {
            _windowActive = true;
            ApplyTimerState();
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            _windowActive = false;
            timer.Stop();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            ApplyTimerState();
        }

        private void ApplyTimerState()
        {
            var shouldRun = _windowActive &&
                            window.WindowState != WindowState.Minimized &&
                            LivePages.Contains(main.CurrentPage);

            if (shouldRun)
            {
                if (!timer.IsEnabled)
                    timer.Start();
                return;
            }

            if (timer.IsEnabled)
                timer.Stop();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Activated -= OnActivated;
            window.Deactivated -= OnDeactivated;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnClosed;
            timer.Stop();
        }
    }
}
