using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
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
                break;
        }
    }

    private void QueueImmediateLiveDashboardRefresh()
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () => await UpdateLiveDashboardAsync()));
    }
}
