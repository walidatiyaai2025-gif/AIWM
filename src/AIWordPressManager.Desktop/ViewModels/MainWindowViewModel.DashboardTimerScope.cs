using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _immediateLiveDashboardRefreshQueued;

    /// <summary>
    /// Keeps the existing live dashboard timer page-scoped. Heavy runtime metrics,
    /// job reloads, and dashboard collection rebuilds no longer continue while the
    /// user is working in unrelated screens.
    /// </summary>
    partial void OnCurrentPageChanged(string value)
    {
        if (_liveDashboardTimer is null)
            return;

        switch (value)
        {
            case "Dashboard":
                _liveDashboardTimer.Interval = TimeSpan.FromSeconds(1);
                if (!_liveDashboardTimer.IsEnabled)
                    _liveDashboardTimer.Start();

                QueueImmediateLiveDashboardRefresh();
                break;

            case "Performance":
                // Performance needs runtime metrics, but not a one-second refresh.
                _liveDashboardTimer.Interval = TimeSpan.FromSeconds(5);
                if (!_liveDashboardTimer.IsEnabled)
                    _liveDashboardTimer.Start();

                QueueImmediateLiveDashboardRefresh();
                break;

            default:
                _liveDashboardTimer.Stop();
                _immediateLiveDashboardRefreshQueued = false;
                break;
        }
    }

    private void QueueImmediateLiveDashboardRefresh()
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (_immediateLiveDashboardRefreshQueued || dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        _immediateLiveDashboardRefreshQueued = true;
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () =>
            {
                try
                {
                    if (CurrentPage is "Dashboard" or "Performance")
                        await UpdateLiveDashboardAsync();
                }
                finally
                {
                    _immediateLiveDashboardRefreshQueued = false;
                }
            }));
    }
}
