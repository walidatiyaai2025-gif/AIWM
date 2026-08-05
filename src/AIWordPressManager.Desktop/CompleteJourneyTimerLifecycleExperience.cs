using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the existing complete-journey refresh timer active only while the Dashboard
/// is visible and the main window is active. No journey business logic is changed.
/// </summary>
internal static class CompleteJourneyTimerLifecycleExperience
{
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
        if (window.DataContext is not MainWindowViewModel main) return;

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            var timer = ResolveJourneyTimer();
            if (timer is null) return;

            var state = new State(window, main, timer);
            Attached.Add(window, state);
            state.Attach();
        }), DispatcherPriority.ContextIdle);
    }

    private static DispatcherTimer? ResolveJourneyTimer()
    {
        var type = typeof(MainWindow).Assembly.GetType("AIWordPressManager.Desktop.CompleteUserJourneyBootstrap");
        var field = type?.GetField("RefreshTimer", BindingFlags.Static | BindingFlags.NonPublic);
        return field?.GetValue(null) as DispatcherTimer;
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
            Apply();
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                Apply();
        }

        private void OnActivated(object? sender, EventArgs e)
        {
            _windowActive = true;
            Apply();
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            _windowActive = false;
            timer.Stop();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e) => Apply();

        private void Apply()
        {
            var shouldRun = _windowActive &&
                            window.WindowState != WindowState.Minimized &&
                            main.CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase);

            if (shouldRun)
            {
                if (!timer.IsEnabled)
                    timer.Start();
            }
            else if (timer.IsEnabled)
            {
                timer.Stop();
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            timer.Stop();
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Activated -= OnActivated;
            window.Deactivated -= OnDeactivated;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnClosed;
        }
    }
}
