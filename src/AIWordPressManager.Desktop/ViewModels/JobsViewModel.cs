using System.Collections.ObjectModel;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class JobsViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobCancellationRegistry _cancellationRegistry;
    private readonly IDialogService _dialogs;
    private readonly SitesViewModel _sites;
    private readonly DispatcherTimer _pollTimer;
    private readonly DateTime _sessionStartedUtc = DateTime.UtcNow;
    private bool _disposed;

    public ObservableCollection<JobRow> Items { get; } = [];
    public ObservableCollection<string> StatusFilters { get; } = ["All", "Running", "Waiting", "Paused", "Completed", "Failed", "Cancelled"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CancelSelectedCommand { get; }
    public IAsyncRelayCommand RetrySelectedCommand { get; }
    public IRelayCommand ClearFilterCommand { get; }
    public IRelayCommand ToggleAutoRefreshCommand { get; }
    public IRelayCommand ShowFailedCommand { get; }

    [ObservableProperty] private JobRow? _selectedItem;
    [ObservableProperty] private string _selectedStatus = "All";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _currentStep = "Reading job history from SQLite.";
    [ObservableProperty] private string _statusMessage = "Background jobs are stored locally and refresh automatically.";
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private bool _autoRefreshEnabled = true;
    [ObservableProperty] private int _nextRefreshSeconds = 3;

    public int RunningCount => Items.Count(x => x.Status.Equals("Running", StringComparison.OrdinalIgnoreCase));
    public int CompletedCount => Items.Count(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
    public int FailedCount => Items.Count(x => x.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
    public int CancelledCount => Items.Count(x => x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
    public int WaitingCount => Items.Count(x => x.Status.Equals("Waiting", StringComparison.OrdinalIgnoreCase));
    public int PausedCount => Items.Count(x => x.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase));
    public int StaleRunningCount => Items.Count(x => x.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - x.UpdatedAtUtc > TimeSpan.FromMinutes(5));
    public string QueueHealthText => FailedCount > 0 ? $"Attention required: {FailedCount} failed job(s)." : StaleRunningCount > 0 ? $"{StaleRunningCount} running job(s) have not reported progress recently." : RunningCount > 0 ? $"Healthy: {RunningCount} job(s) are running." : "Queue is healthy and idle.";
    public string AutoRefreshText => AutoRefreshEnabled ? $"Auto refresh in {NextRefreshSeconds}s" : "Auto refresh paused";
    public IEnumerable<JobRow> FilteredItems => Items.Where(MatchesFilter);

    public JobsViewModel(
        IServiceScopeFactory scopeFactory,
        IJobCancellationRegistry cancellationRegistry,
        IDialogService dialogs,
        SitesViewModel sites)
    {
        _scopeFactory = scopeFactory;
        _cancellationRegistry = cancellationRegistry;
        _dialogs = dialogs;
        _sites = sites;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CancelSelectedCommand = new AsyncRelayCommand(CancelSelectedAsync, CanCancelSelected);
        RetrySelectedCommand = new AsyncRelayCommand(RetrySelectedAsync, CanRetrySelected);
        ClearFilterCommand = new RelayCommand(() =>
        {
            SelectedStatus = "All";
            SearchText = string.Empty;
        });
        ToggleAutoRefreshCommand = new RelayCommand(() =>
        {
            AutoRefreshEnabled = !AutoRefreshEnabled;
            NextRefreshSeconds = 3;
            StatusMessage = AutoRefreshEnabled ? "Automatic queue refresh resumed." : "Automatic queue refresh paused. Manual refresh remains available.";
        });
        ShowFailedCommand = new RelayCommand(() => SelectedStatus = "Failed");

        _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += async (_, _) =>
        {
            if (!AutoRefreshEnabled || IsBusy) return;
            NextRefreshSeconds--;
            OnPropertyChanged(nameof(AutoRefreshText));
            if (NextRefreshSeconds > 0) return;
            NextRefreshSeconds = 3;
            await LoadAsync(silent: true);
        };
        _pollTimer.Start();

        _sites.SelectedSiteChanged += (_, _) => _ = LoadAsync();
    }

    partial void OnSelectedItemChanged(JobRow? value) => NotifyCommands();
    partial void OnSelectedStatusChanged(string value) => OnPropertyChanged(nameof(FilteredItems));
    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredItems));
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnAutoRefreshEnabledChanged(bool value) => OnPropertyChanged(nameof(AutoRefreshText));
    partial void OnNextRefreshSecondsChanged(int value) => OnPropertyChanged(nameof(AutoRefreshText));

    public Task LoadAsync() => LoadAsync(silent: false);

    private async Task LoadAsync(bool silent)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true;
        if (!silent)
        {
            ProgressPercent = 20;
            CurrentStep = "Opening SQLite job history";
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IExecutionJobStore>();
            var siteId = _sites.SelectedSite?.Id;
            var rows = await store.GetRecentAsync(siteId, 300);

            var selectedId = SelectedItem?.Id;
            Items.Clear();
            foreach (var row in rows)
            {
                Items.Add(JobRow.From(row, _cancellationRegistry.IsRegistered(row.Id)));
            }

            SelectedItem = selectedId.HasValue ? Items.FirstOrDefault(x => x.Id == selectedId.Value) : null;
            ProgressPercent = 100;
            CurrentStep = "Job history ready";
            StatusMessage = siteId.HasValue
                ? $"Loaded {Items.Count} jobs for {_sites.SelectedSite!.Name}. Running: {RunningCount}; completed: {CompletedCount}; failed: {FailedCount}; cancelled: {CancelledCount}."
                : $"Loaded {Items.Count} jobs across all sites.";

            UnreadCount = Items.Count(x =>
                x.UpdatedAtUtc >= _sessionStartedUtc &&
                (x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
                 x.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                 x.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)));

            RaiseDerivedProperties();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string BuildNotificationSummary()
    {
        var recent = Items
            .Where(x => x.UpdatedAtUtc >= _sessionStartedUtc)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(8)
            .ToArray();

        if (recent.Length == 0) return "There are no new job notifications in this session.";

        var lines = recent.Select(x =>
            $"• {x.JobType} — {x.Status} ({x.ProgressPercent}%)\n  {x.CurrentStep}");
        return string.Join("\n\n", lines);
    }

    public void MarkNotificationsRead() => UnreadCount = 0;

    private bool MatchesFilter(JobRow row)
    {
        if (!SelectedStatus.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !row.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase)) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return row.JobType.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               row.SiteName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               row.CurrentStep.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               (row.ErrorDetails?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool CanCancelSelected() => SelectedItem?.CanCancel == true && !IsBusy;

    private async Task CancelSelectedAsync()
    {
        if (SelectedItem is null) return;
        var confirmed = await _dialogs.ConfirmAsync(
            "Cancel background job",
            $"Cancel {SelectedItem.JobType} for {SelectedItem.SiteName}?\n\nThe operation will receive a cancellation signal and keep any already committed audit history.");
        if (!confirmed) return;

        if (!_cancellationRegistry.TryCancel(SelectedItem.Id))
        {
            await _dialogs.ShowInformationAsync(
                "Job cannot be cancelled from this session",
                "The job is no longer registered in the running process. Refresh the list; if it remains Running after an application restart, review its log before retrying.");
            return;
        }

        StatusMessage = $"Cancellation requested for {SelectedItem.JobType}.";
        await Task.Delay(250);
        await LoadAsync();
    }

    private bool CanRetrySelected() =>
        SelectedItem is not null &&
        !IsBusy &&
        (SelectedItem.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
         SelectedItem.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) &&
        SelectedItem.JobType.Equals("WordPressSync", StringComparison.OrdinalIgnoreCase);

    private async Task RetrySelectedAsync()
    {
        if (SelectedItem is null) return;
        var confirmed = await _dialogs.ConfirmAsync(
            "Retry synchronization",
            $"Start a new WordPress synchronization job for {SelectedItem.SiteName}? The previous job record will remain in history.");
        if (!confirmed) return;

        IsBusy = true;
        ProgressPercent = 5;
        CurrentStep = "Starting retry";
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var synchronization = scope.ServiceProvider.GetRequiredService<IWordPressSynchronizationService>();
            var progress = new Progress<WordPressSyncProgress>(value =>
            {
                ProgressPercent = value.Percent;
                CurrentStep = value.Step;
            });

            var result = await synchronization.SynchronizeAsync(SelectedItem.SiteId, progress);
            StatusMessage = result.IsSuccess
                ? "Synchronization retry completed and the offline snapshot was refreshed."
                : result.Error.Message;

            if (result.IsFailure)
                await _dialogs.ShowErrorAsync("Retry failed", result.Error.Message);
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private void RaiseDerivedProperties()
    {
        OnPropertyChanged(nameof(RunningCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(CancelledCount));
        OnPropertyChanged(nameof(WaitingCount));
        OnPropertyChanged(nameof(PausedCount));
        OnPropertyChanged(nameof(StaleRunningCount));
        OnPropertyChanged(nameof(QueueHealthText));
        OnPropertyChanged(nameof(AutoRefreshText));
        OnPropertyChanged(nameof(FilteredItems));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CancelSelectedCommand.NotifyCanExecuteChanged();
        RetrySelectedCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
    }
}

public sealed record JobRow(
    Guid Id,
    Guid SiteId,
    string SiteName,
    string JobType,
    string Status,
    int ProgressPercent,
    string CurrentStep,
    string? ErrorDetails,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime UpdatedAtUtc,
    bool CanCancel)
{
    public string DurationText
    {
        get
        {
            var end = CompletedAtUtc ?? DateTime.UtcNow;
            var duration = end - StartedAtUtc;
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                : duration.TotalMinutes >= 1
                    ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                    : $"{Math.Max(0, duration.Seconds)}s";
        }
    }

    public static JobRow From(ExecutionJobListItem row, bool canCancel) => new(
        row.Id,
        row.SiteId,
        row.SiteName,
        row.JobType,
        row.Status,
        row.ProgressPercent,
        row.CurrentStep,
        row.ErrorDetails,
        row.StartedAtUtc,
        row.CompletedAtUtc,
        row.UpdatedAtUtc,
        canCancel && row.Status.Equals("Running", StringComparison.OrdinalIgnoreCase));
}
