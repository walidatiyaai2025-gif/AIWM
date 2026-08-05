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
/// refreshed at dispatcher idle priority.
/// </summary>
internal static class SelectedSiteNavigationCoordinator
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly HashSet<string> PagesWithoutSiteHydration = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard",
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
        private bool _disposed;
        private bool _refreshPending;

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

            var selected = main.Sites.SelectedSite;
            main.InvalidateNavigationLoadCache();
            main.ConnectionStatus = selected is null
                ? "No site selected"
                : $"{selected.Name} • {selected.Status}";
            main.DashboardSelectedSite = selected?.Name ?? "No site selected";
            main.ApplicationDataStatus = selected is null
                ? "No site selected."
                : $"{selected.Name} selected. The active workspace will refresh in the background.";

            if (selected is null || PagesWithoutSiteHydration.Contains(main.CurrentPage))
                return;

            ScheduleActivePageRefresh();
        }

        private void ScheduleActivePageRefresh()
        {
            if (_refreshPending || _disposed)
                return;

            _refreshPending = true;
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                _refreshPending = false;
                if (_disposed || !window.IsLoaded || main.Sites.SelectedSite is null)
                    return;

                var page = main.CurrentPage;
                if (PagesWithoutSiteHydration.Contains(page))
                    return;

                _ = main.NavigateCommand.ExecuteAsync(page);
            }));
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            main.Sites.SelectedSiteChanged -= OnSelectedSiteChanged;
            window.Closed -= OnClosed;
            _removedLegacyHandlers.Clear();
        }
    }
}
