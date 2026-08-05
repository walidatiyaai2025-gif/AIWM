using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Replaces the legacy site-change handler that hydrated several workspaces at once.
/// The selected card and header update immediately; only the active workspace is then
/// refreshed at dispatcher idle priority. Pending refreshes are versioned so a result
/// for a previously selected site cannot win after a rapid card change.
/// </summary>
internal static class SelectedSiteNavigationCoordinator
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly HashSet<string> PagesWithoutSiteHydration = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sites",
        "Settings",
        "Help",
        "Performance",
        "Notification Center",
        "Activity Timeline",
        "Release Readiness"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
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
        private readonly List<EventHandler> _removedLegacyHandlers = [];
        private DispatcherOperation? _pendingRefresh;
        private long _selectionVersion;
        private bool _disposed;

        public void Attach()
        {
            RemoveLegacyMainWindowHandler();
            main.Sites.SelectedSiteChanged += OnSelectedSiteChanged;
            window.Closed += OnClosed;
        }

        private void RemoveLegacyMainWindowHandler()
        {
            var eventField = typeof(SitesViewModel).GetField(
                "SelectedSiteChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (eventField?.GetValue(main.Sites) is not EventHandler handlers)
                return;

            foreach (var handler in handlers.GetInvocationList().OfType<EventHandler>())
            {
                if (!ReferenceEquals(handler.Target, main))
                    continue;

                main.Sites.SelectedSiteChanged -= handler;
                _removedLegacyHandlers.Add(handler);
            }
        }

        private void OnSelectedSiteChanged(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            var version = Interlocked.Increment(ref _selectionVersion);
            CancelPendingRefresh();

            var selected = main.Sites.SelectedSite;
            main.InvalidateNavigationLoadCache();
            main.ConnectionStatus = selected is null
                ? "No site selected"
                : $"{selected.Name} • {selected.Status}";
            main.DashboardSelectedSite = selected?.Name ?? "No site selected";
            main.ApplicationDataStatus = selected is null
                ? "No site selected."
                : $"{selected.Name} selected. The active workspace will refresh quietly.";

            main.StartOptimizationCommand.NotifyCanExecuteChanged();
            main.ContinueJourneyCommand.NotifyCanExecuteChanged();
            main.RunSafeAutopilotCommand.NotifyCanExecuteChanged();

            if (selected is null)
                return;

            // Dashboard navigation only recalculates its cached summary and does not hydrate
            // all site modules. Other utility pages do not need a site-specific refresh.
            if (main.CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
            {
                ScheduleActivePageRefresh(version, selected.Id);
                return;
            }

            if (PagesWithoutSiteHydration.Contains(main.CurrentPage))
                return;

            ScheduleActivePageRefresh(version, selected.Id);
        }

        private void ScheduleActivePageRefresh(long version, Guid selectedSiteId)
        {
            _pendingRefresh = window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    _pendingRefresh = null;
                    if (_disposed || !window.IsLoaded)
                        return;

                    if (version != Volatile.Read(ref _selectionVersion))
                        return;

                    var selected = main.Sites.SelectedSite;
                    if (selected is null || selected.Id != selectedSiteId)
                        return;

                    var page = main.CurrentPage;
                    if (!page.Equals("Dashboard", StringComparison.OrdinalIgnoreCase) &&
                        PagesWithoutSiteHydration.Contains(page))
                        return;

                    _ = main.NavigateCommand.ExecuteAsync(page);
                }));
        }

        private void CancelPendingRefresh()
        {
            if (_pendingRefresh is null)
                return;

            if (_pendingRefresh.Status == DispatcherOperationStatus.Pending)
                _pendingRefresh.Abort();

            _pendingRefresh = null;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            Interlocked.Increment(ref _selectionVersion);
            CancelPendingRefresh();
            main.Sites.SelectedSiteChanged -= OnSelectedSiteChanged;
            window.Closed -= OnClosed;
            _removedLegacyHandlers.Clear();
        }
    }
}
