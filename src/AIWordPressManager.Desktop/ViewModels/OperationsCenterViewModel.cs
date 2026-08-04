using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class OperationsCenterViewModel : ObservableObject, IDisposable
{
    private readonly JobsViewModel _jobs;
    private readonly AiDecisionCenterViewModel _decisions;
    private readonly EvidenceCenterViewModel _evidence;
    private readonly SchedulerCenterViewModel _scheduler;
    private readonly TransactionCenterViewModel _transactions;
    private readonly DispatcherTimer _timer;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastSampleUtc;
    private bool _disposed;

    public event Action<string>? NavigationRequested;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<string?> OpenCommand { get; }
    public IRelayCommand ToggleLiveCommand { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _liveRefreshEnabled = true;
    [ObservableProperty] private string _status = "Operations telemetry is ready.";
    [ObservableProperty] private DateTime? _lastRefreshUtc;
    [ObservableProperty] private double _applicationMemoryMb;
    [ObservableProperty] private double _applicationCpuPercent;
    [ObservableProperty] private int _runningJobs;
    [ObservableProperty] private int _waitingJobs;
    [ObservableProperty] private int _failedJobs;
    [ObservableProperty] private int _executableDecisions;
    [ObservableProperty] private int _approvalDecisions;
    [ObservableProperty] private int _protectedDecisions;
    [ObservableProperty] private int _evidenceFiles;
    [ObservableProperty] private int _verifiedEvidencePairs;
    [ObservableProperty] private int _enabledSchedules;
    [ObservableProperty] private int _failedSchedules;
    [ObservableProperty] private int _committedTransactions;
    [ObservableProperty] private int _interruptedTransactions;
    [ObservableProperty] private string _queueHealth = "Not evaluated";
    [ObservableProperty] private string _nextSchedule = "No scheduled work";
    [ObservableProperty] private string _operationsHealth = "Initializing";

    public string LastRefreshText => LastRefreshUtc is null ? "Not refreshed" : LastRefreshUtc.Value.ToLocalTime().ToString("T");
    public string LiveRefreshText => LiveRefreshEnabled ? "LIVE • refreshes every 5 seconds" : "Live refresh paused";

    public OperationsCenterViewModel(
        JobsViewModel jobs,
        AiDecisionCenterViewModel decisions,
        EvidenceCenterViewModel evidence,
        SchedulerCenterViewModel scheduler,
        TransactionCenterViewModel transactions)
    {
        _jobs = jobs;
        _decisions = decisions;
        _evidence = evidence;
        _scheduler = scheduler;
        _transactions = transactions;

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(silent: false), () => !IsBusy);
        OpenCommand = new RelayCommand<string?>(destination =>
        {
            if (!string.IsNullOrWhiteSpace(destination)) NavigationRequested?.Invoke(destination);
        });
        ToggleLiveCommand = new RelayCommand(() =>
        {
            LiveRefreshEnabled = !LiveRefreshEnabled;
            Status = LiveRefreshEnabled ? "Live operations telemetry resumed." : "Live operations telemetry paused; manual refresh is still available.";
        });

        var process = Process.GetCurrentProcess();
        _lastProcessorTime = process.TotalProcessorTime;
        _lastSampleUtc = DateTime.UtcNow;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) =>
        {
            if (LiveRefreshEnabled && !IsBusy) await RefreshAsync(silent: true);
        };
        _timer.Start();
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();
    partial void OnLiveRefreshEnabledChanged(bool value) => OnPropertyChanged(nameof(LiveRefreshText));
    partial void OnLastRefreshUtcChanged(DateTime? value) => OnPropertyChanged(nameof(LastRefreshText));

    public Task LoadAsync() => RefreshAsync(silent: false);

    private async Task RefreshAsync(bool silent = false)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        try
        {
            if (!silent) Status = "Refreshing jobs, decisions, schedules, evidence, and transaction telemetry...";
            await _jobs.LoadAsync();
            await _decisions.LoadAsync();
            await _evidence.LoadAsync();
            await _scheduler.LoadAsync();
            await _transactions.LoadAsync();

            RunningJobs = _jobs.RunningCount;
            WaitingJobs = _jobs.WaitingCount + _jobs.PausedCount;
            FailedJobs = _jobs.FailedCount;
            QueueHealth = _jobs.QueueHealthText;

            ExecutableDecisions = _decisions.ExecuteCount;
            ApprovalDecisions = _decisions.ApprovalCount;
            ProtectedDecisions = _decisions.ProtectedCount;

            EvidenceFiles = _evidence.TotalCount;
            VerifiedEvidencePairs = _evidence.VerifiedPairCount;
            EnabledSchedules = _scheduler.EnabledCount;
            FailedSchedules = _scheduler.FailedCount;
            NextSchedule = _scheduler.NextRunSummary;
            CommittedTransactions = _transactions.CommittedCount;
            InterruptedTransactions = _transactions.InterruptedCount;

            SampleProcess();
            OperationsHealth = BuildHealthSummary();
            LastRefreshUtc = DateTime.UtcNow;
            Status = $"Operations snapshot refreshed. {OperationsHealth}";
        }
        catch (Exception ex)
        {
            Status = $"Operations telemetry refresh failed: {ex.Message}";
            OperationsHealth = "Attention required";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SampleProcess()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        ApplicationMemoryMb = process.WorkingSet64 / 1024d / 1024d;
        var now = DateTime.UtcNow;
        var elapsedMs = Math.Max(1d, (now - _lastSampleUtc).TotalMilliseconds);
        var cpuMs = (process.TotalProcessorTime - _lastProcessorTime).TotalMilliseconds;
        ApplicationCpuPercent = Math.Clamp(cpuMs / (elapsedMs * Environment.ProcessorCount) * 100d, 0d, 100d);
        _lastProcessorTime = process.TotalProcessorTime;
        _lastSampleUtc = now;
    }

    private string BuildHealthSummary()
    {
        if (InterruptedTransactions > 0) return $"Recovery required: {InterruptedTransactions} interrupted transaction(s).";
        if (FailedJobs > 0 || FailedSchedules > 0) return $"Attention required: {FailedJobs} failed job(s), {FailedSchedules} failed schedule(s).";
        if (ProtectedDecisions > 0) return $"Protected: {ProtectedDecisions} AI decision(s) require staging or a supported adapter.";
        if (RunningJobs > 0) return $"Healthy: {RunningJobs} job(s) are running.";
        return "Healthy and idle.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
