using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private DispatcherTimer? _dashboardClockTimer;
    private DispatcherTimer? _dashboardMetricsTimer;
    private DispatcherTimer? _dashboardDataTimer;
    private bool _dashboardDataRefreshBusy;
    private bool _optimizedDashboardTimersConfigured;

    /// <summary>
    /// Replaces the legacy all-in-one dashboard timer with page-scoped timers.
    /// Clock rendering remains responsive while SQLite/job refreshes run much less often.
    /// </summary>
    private void ConfigureOptimizedDashboardTimers()
    {
        if (_optimizedDashboardTimersConfigured)
            return;

        _optimizedDashboardTimersConfigured = true;

        // The constructor starts this legacy timer. Keep its handler intact, but stop it
        // permanently so it cannot combine metrics, Jobs.LoadAsync and full dashboard
        // collection rebuilds in the same frequent tick.
        _liveDashboardTimer.Stop();

        _dashboardClockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dashboardClockTimer.Tick += (_, _) =>
        {
            if (CurrentPage != "Dashboard")
                return;

            DashboardLiveClock = DateTime.Now.ToString("HH:mm:ss");
            DashboardPulseOn = !DashboardPulseOn;
        };

        _dashboardMetricsTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _dashboardMetricsTimer.Tick += (_, _) =>
        {
            if (CurrentPage is not ("Dashboard" or "Performance"))
                return;

            UpdateRuntimeMetrics();
            DashboardSelectedSite = Sites.SelectedSite?.Name ?? "No site selected";
            DashboardExecutionProgress = ExecutionCenter.IsBusy ? ExecutionCenter.ProgressPercent : 0;
            DashboardExecutionStep = ExecutionCenter.IsBusy
                ? $"{ExecutionCenter.QueueState} • {ExecutionCenter.CurrentStep}"
                : "Execution queue idle";
        };

        _dashboardDataTimer = new DispatcherTimer(DispatcherPriority.ContextIdle)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _dashboardDataTimer.Tick += async (_, _) => await RefreshDashboardDataAsync();

        ApplyDashboardTimerScope(CurrentPage);
    }

    partial void OnCurrentPageChanged(string value)
    {
        if (!_optimizedDashboardTimersConfigured)
        {
            // Before fast startup finishes, prevent the legacy timer from running on
            // unrelated pages. ConfigureOptimizedDashboardTimers will take ownership.
            if (value is not ("Dashboard" or "Performance"))
                _liveDashboardTimer.Stop();
            return;
        }

        ApplyDashboardTimerScope(value);
    }

    private void ApplyDashboardTimerScope(string page)
    {
        _dashboardClockTimer?.Stop();
        _dashboardMetricsTimer?.Stop();
        _dashboardDataTimer?.Stop();

        switch (page)
        {
            case "Dashboard":
                DashboardLiveClock = DateTime.Now.ToString("HH:mm:ss");
                _dashboardClockTimer?.Start();
                _dashboardMetricsTimer?.Start();
                _dashboardDataTimer?.Start();
                QueueImmediateDashboardSnapshot();
                break;

            case "Performance":
                _dashboardMetricsTimer?.Start();
                QueueImmediateMetricsSnapshot();
                break;
        }
    }

    private void QueueImmediateDashboardSnapshot()
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(async () =>
            {
                if (CurrentPage != "Dashboard")
                    return;

                UpdateRuntimeMetrics();
                await RefreshDashboardDataAsync();
            }));
    }

    private void QueueImmediateMetricsSnapshot()
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (CurrentPage == "Performance")
                    UpdateRuntimeMetrics();
            }));
    }

    private async Task RefreshDashboardDataAsync()
    {
        if (CurrentPage != "Dashboard" || IsMemoryCooling || _dashboardDataRefreshBusy)
            return;

        _dashboardDataRefreshBusy = true;
        try
        {
            await Jobs.LoadAsync();

            if (CurrentPage != "Dashboard")
                return;

            DashboardRunningJobs = Jobs.RunningCount;
            DashboardCompletedJobs = Jobs.CompletedCount;
            DashboardFailedJobs = Jobs.FailedCount;
            DashboardQueueTotal = Jobs.Items.Count;
            DashboardWorkerState = DashboardRunningJobs > 0
                ? "Processing"
                : DashboardFailedJobs > 0 ? "Attention" : "Idle";

            var latestJob = Jobs.Items.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
            DashboardLastJob = latestJob is null
                ? "No jobs recorded"
                : $"{latestJob.JobType} • {latestJob.Status} • {latestJob.UpdatedAtUtc.ToLocalTime():HH:mm:ss}";
            DashboardLiveStatus = DashboardRunningJobs > 0
                ? $"LIVE • {DashboardRunningJobs} job(s) running"
                : DashboardFailedJobs > 0
                    ? $"LIVE • {DashboardFailedJobs} job(s) need attention"
                    : "LIVE • systems ready";
            DashboardLastRefresh = $"Updated {DateTime.Now:HH:mm:ss}";
            RefreshDashboard();
        }
        catch
        {
            if (CurrentPage == "Dashboard")
                DashboardLiveStatus = "LIVE • refresh delayed";
        }
        finally
        {
            _dashboardDataRefreshBusy = false;
        }
    }
}
