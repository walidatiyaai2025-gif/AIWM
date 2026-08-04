using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SchedulerCenterViewModel : ObservableObject
{
    private readonly SitesViewModel _sites;
    private readonly AutopilotOrchestratorViewModel _orchestrator;
    private readonly SeoAuditViewModel _seoAudit;
    private readonly ContentAuditViewModel _contentAudit;
    private readonly BrokenLinksViewModel _brokenLinks;
    private readonly BackupsViewModel _backups;
    private readonly IApplicationPathService _paths;
    private readonly DispatcherTimer _timer;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private bool _checkingDueTasks;
    private readonly HashSet<Guid> _runningTaskIds = [];

    public ObservableCollection<ScheduledAutomationItem> Items { get; } = [];
    public IReadOnlyList<string> TaskTypes { get; } =
    [
        "Full AI workflow",
        "SEO audit",
        "Content audit",
        "Broken-link scan",
        "Database backup"
    ];
    public IReadOnlyList<string> Frequencies { get; } = ["Daily", "Weekly", "Monthly"];

    [ObservableProperty] private ScheduledAutomationItem? _selectedItem;
    [ObservableProperty] private string _newTaskName = "Nightly AI optimization";
    [ObservableProperty] private string _newTaskType = "Full AI workflow";
    [ObservableProperty] private string _newFrequency = "Daily";
    [ObservableProperty] private string _newTime = "02:00";
    [ObservableProperty] private string _status = "Scheduler is ready";
    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _pausedCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private string _nextRunSummary = "No scheduled work";
    [ObservableProperty] private bool _isBusy;

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand RunNowCommand { get; }
    public IRelayCommand ToggleEnabledCommand { get; }

    public SchedulerCenterViewModel(
        SitesViewModel sites,
        AutopilotOrchestratorViewModel orchestrator,
        SeoAuditViewModel seoAudit,
        ContentAuditViewModel contentAudit,
        BrokenLinksViewModel brokenLinks,
        BackupsViewModel backups,
        IApplicationPathService paths)
    {
        _sites = sites;
        _orchestrator = orchestrator;
        _seoAudit = seoAudit;
        _contentAudit = contentAudit;
        _brokenLinks = brokenLinks;
        _backups = backups;
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        AddCommand = new AsyncRelayCommand(AddAsync, CanAdd);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => SelectedItem is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedItem is not null);
        RunNowCommand = new AsyncRelayCommand(RunSelectedNowAsync, () => SelectedItem is not null && !IsBusy);
        ToggleEnabledCommand = new RelayCommand(ToggleEnabled, () => SelectedItem is not null);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RunDueTasksAsync();
        _timer.Start();
        _sites.SelectedSiteChanged += async (_, _) => await LoadAsync();
    }

    partial void OnSelectedItemChanged(ScheduledAutomationItem? value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RunNowCommand.NotifyCanExecuteChanged();
        ToggleEnabledCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewTaskNameChanged(string value) => AddCommand.NotifyCanExecuteChanged();
    partial void OnNewTimeChanged(string value) => AddCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => RunNowCommand.NotifyCanExecuteChanged();

    private bool CanAdd() => _sites.SelectedSite is not null &&
                             !string.IsNullOrWhiteSpace(NewTaskName) &&
                             TimeOnly.TryParse(NewTime, out _);

    public async Task LoadAsync()
    {
        Items.Clear();
        if (_sites.SelectedSite is null)
        {
            Status = "Select a site before configuring scheduled automation.";
            RefreshSummary();
            return;
        }

        try
        {
            var path = GetSchedulePath();
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                var saved = JsonSerializer.Deserialize<List<ScheduledAutomationSnapshot>>(json, _jsonOptions) ?? [];
                foreach (var snapshot in saved.OrderBy(x => x.NextRunUtc))
                    Items.Add(ScheduledAutomationItem.FromSnapshot(snapshot));
            }

            Status = Items.Count == 0
                ? "No tasks are scheduled for this site. Add the first automation below."
                : $"Loaded {Items.Count} scheduled task(s) for {_sites.SelectedSite.Name}.";
        }
        catch (Exception exception)
        {
            Status = $"Schedule could not be loaded: {exception.Message}";
        }
        RefreshSummary();
    }

    private async Task AddAsync()
    {
        if (_sites.SelectedSite is null || !TimeOnly.TryParse(NewTime, out var time)) return;
        var item = new ScheduledAutomationItem
        {
            Id = Guid.NewGuid(),
            Name = NewTaskName.Trim(),
            TaskType = NewTaskType,
            Frequency = NewFrequency,
            LocalTime = time.ToString("HH:mm"),
            IsEnabled = true,
            NextRunUtc = CalculateNextRunUtc(NewFrequency, time, DateTimeOffset.Now),
            Status = "Scheduled"
        };
        Items.Add(item);
        SelectedItem = item;
        await PersistAsync();
        Status = $"Scheduled '{item.Name}' for {item.NextRunLocalText}.";
        RefreshSummary();
    }

    private async Task SaveAsync()
    {
        if (SelectedItem is null) return;
        if (!TimeOnly.TryParse(SelectedItem.LocalTime, out var time))
        {
            Status = "Enter a valid time using HH:mm.";
            return;
        }
        SelectedItem.NextRunUtc = CalculateNextRunUtc(SelectedItem.Frequency, time, DateTimeOffset.Now);
        SelectedItem.Status = SelectedItem.IsEnabled ? "Scheduled" : "Paused";
        await PersistAsync();
        Status = $"Saved '{SelectedItem.Name}'.";
        RefreshSummary();
    }

    private async Task DeleteAsync()
    {
        if (SelectedItem is null) return;
        var name = SelectedItem.Name;
        Items.Remove(SelectedItem);
        SelectedItem = null;
        await PersistAsync();
        Status = $"Deleted schedule '{name}'.";
        RefreshSummary();
    }

    private async void ToggleEnabled()
    {
        if (SelectedItem is null) return;
        SelectedItem.IsEnabled = !SelectedItem.IsEnabled;
        SelectedItem.Status = SelectedItem.IsEnabled ? "Scheduled" : "Paused";
        if (SelectedItem.IsEnabled && TimeOnly.TryParse(SelectedItem.LocalTime, out var time))
            SelectedItem.NextRunUtc = CalculateNextRunUtc(SelectedItem.Frequency, time, DateTimeOffset.Now);
        await PersistAsync();
        RefreshSummary();
    }

    private async Task RunSelectedNowAsync()
    {
        if (SelectedItem is null) return;
        await ExecuteTaskAsync(SelectedItem, false);
    }

    private async Task RunDueTasksAsync()
    {
        if (_checkingDueTasks || IsBusy || _sites.SelectedSite is null) return;
        _checkingDueTasks = true;
        try
        {
            var due = Items.Where(x => x.IsEnabled && x.NextRunUtc <= DateTimeOffset.UtcNow).ToList();
            foreach (var item in due)
                await ExecuteTaskAsync(item, true);
        }
        finally
        {
            _checkingDueTasks = false;
        }
    }

    private async Task ExecuteTaskAsync(ScheduledAutomationItem item, bool scheduledRun)
    {
        if (!_runningTaskIds.Add(item.Id))
        {
            Status = $"'{item.Name}' is already running. Duplicate execution was prevented.";
            return;
        }

        var startedUtc = DateTimeOffset.UtcNow;
        var outcome = "Failed";
        var details = string.Empty;
        IsBusy = true;
        item.Status = "Running";
        item.LastMessage = scheduledRun ? "Started automatically" : "Started manually";
        Status = $"Running {item.TaskType}: {item.Name}";
        try
        {
            switch (item.TaskType)
            {
                case "Full AI workflow": await _orchestrator.RunFullWorkflowCommand.ExecuteAsync(null); break;
                case "SEO audit": await _seoAudit.RunAuditCommand.ExecuteAsync(null); break;
                case "Content audit": await _contentAudit.RunAuditCommand.ExecuteAsync(null); break;
                case "Broken-link scan": await _brokenLinks.RunScanCommand.ExecuteAsync(null); break;
                case "Database backup": await _backups.CreateBackupCommand.ExecuteAsync(null); break;
                default: throw new InvalidOperationException($"Unsupported scheduled task type: {item.TaskType}");
            }

            item.LastRunUtc = DateTimeOffset.UtcNow;
            item.LastSuccessUtc = item.LastRunUtc;
            item.ConsecutiveFailures = 0;
            item.Status = "Completed";
            item.LastMessage = "Completed successfully";
            outcome = "Completed";
            details = item.LastMessage;
        }
        catch (Exception exception)
        {
            item.LastRunUtc = DateTimeOffset.UtcNow;
            item.ConsecutiveFailures++;
            item.Status = "Failed";
            item.LastMessage = exception.Message;
            outcome = "Failed";
            details = exception.Message;
            if (item.ConsecutiveFailures >= 3)
            {
                item.IsEnabled = false;
                item.Status = "Paused after failures";
            }
        }
        finally
        {
            if (TimeOnly.TryParse(item.LocalTime, out var time))
                item.NextRunUtc = CalculateNextRunUtc(item.Frequency, time, DateTimeOffset.Now.AddMinutes(1));
            await PersistAsync();
            RefreshSummary();
            Status = $"{item.Name}: {item.Status}. {item.LastMessage}";
            await AppendRunHistoryAsync(item, startedUtc, DateTimeOffset.UtcNow, scheduledRun, outcome, details);
            _runningTaskIds.Remove(item.Id);
            IsBusy = _runningTaskIds.Count > 0;
        }
    }

    private async Task PersistAsync()
    {
        if (_sites.SelectedSite is null) return;
        var directory = Path.GetDirectoryName(GetSchedulePath());
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var snapshots = Items.Select(x => x.ToSnapshot()).ToList();
        var json = JsonSerializer.Serialize(snapshots, _jsonOptions);
        await File.WriteAllTextAsync(GetSchedulePath(), json);
    }

    private string GetSchedulePath()
    {
        var siteId = _sites.SelectedSite?.Id ?? Guid.Empty;
        return Path.Combine(_paths.GetApplicationDataDirectory(), "Scheduler", $"schedule-{siteId:N}.json");
    }

    private void RefreshSummary()
    {
        EnabledCount = Items.Count(x => x.IsEnabled);
        PausedCount = Items.Count(x => !x.IsEnabled);
        FailedCount = Items.Count(x => x.Status.Contains("Failed", StringComparison.OrdinalIgnoreCase));
        var next = Items.Where(x => x.IsEnabled).OrderBy(x => x.NextRunUtc).FirstOrDefault();
        NextRunSummary = next is null ? "No scheduled work" : $"{next.Name} • {next.NextRunLocalText}";
    }

    private async Task AppendRunHistoryAsync(
        ScheduledAutomationItem item,
        DateTimeOffset startedUtc,
        DateTimeOffset finishedUtc,
        bool scheduledRun,
        string outcome,
        string details)
    {
        try
        {
            var directory = Path.Combine(_paths.GetApplicationDataDirectory(), "Scheduler", "History");
            Directory.CreateDirectory(directory);
            var siteId = _sites.SelectedSite?.Id ?? Guid.Empty;
            var path = Path.Combine(directory, $"scheduler-history-{siteId:N}.jsonl");
            var record = new SchedulerRunHistoryRecord(
                Guid.NewGuid(),
                item.Id,
                _sites.SelectedSite?.Name ?? "Unknown site",
                item.Name,
                item.TaskType,
                scheduledRun ? "Scheduled" : "Manual",
                startedUtc,
                finishedUtc,
                outcome,
                details);
            var json = JsonSerializer.Serialize(record, _jsonOptions);
            await File.AppendAllTextAsync(path, json + Environment.NewLine);
        }
        catch
        {
            // Scheduler logging must never prevent the scheduled task from completing.
        }
    }

    private static DateTimeOffset CalculateNextRunUtc(string frequency, TimeOnly time, DateTimeOffset from)
    {
        var local = new DateTimeOffset(from.Year, from.Month, from.Day, time.Hour, time.Minute, 0, from.Offset);
        if (local <= from) local = local.AddDays(1);
        return (frequency switch
        {
            "Weekly" => local.AddDays(((int)DayOfWeek.Sunday - (int)local.DayOfWeek + 7) % 7),
            "Monthly" => new DateTimeOffset(local.Year, local.Month, 1, time.Hour, time.Minute, 0, local.Offset).AddMonths(1),
            _ => local
        }).ToUniversalTime();
    }
}

public sealed partial class ScheduledAutomationItem : ObservableObject
{
    public Guid Id { get; set; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _taskType = "Full AI workflow";
    [ObservableProperty] private string _frequency = "Daily";
    [ObservableProperty] private string _localTime = "02:00";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _status = "Scheduled";
    [ObservableProperty] private string _lastMessage = "Not run yet";
    [ObservableProperty] private int _consecutiveFailures;
    [ObservableProperty] private DateTimeOffset _nextRunUtc;
    [ObservableProperty] private DateTimeOffset? _lastRunUtc;
    [ObservableProperty] private DateTimeOffset? _lastSuccessUtc;

    public string NextRunLocalText => NextRunUtc == default ? "Not scheduled" : NextRunUtc.ToLocalTime().ToString("g");
    public string LastRunLocalText => LastRunUtc?.ToLocalTime().ToString("g") ?? "Never";

    partial void OnNextRunUtcChanged(DateTimeOffset value) => OnPropertyChanged(nameof(NextRunLocalText));
    partial void OnLastRunUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastRunLocalText));

    public ScheduledAutomationSnapshot ToSnapshot() => new(
        Id, Name, TaskType, Frequency, LocalTime, IsEnabled, Status, LastMessage,
        ConsecutiveFailures, NextRunUtc, LastRunUtc, LastSuccessUtc);

    public static ScheduledAutomationItem FromSnapshot(ScheduledAutomationSnapshot value) => new()
    {
        Id = value.Id,
        Name = value.Name,
        TaskType = value.TaskType,
        Frequency = value.Frequency,
        LocalTime = value.LocalTime,
        IsEnabled = value.IsEnabled,
        Status = value.Status,
        LastMessage = value.LastMessage,
        ConsecutiveFailures = value.ConsecutiveFailures,
        NextRunUtc = value.NextRunUtc,
        LastRunUtc = value.LastRunUtc,
        LastSuccessUtc = value.LastSuccessUtc
    };
}

public sealed record ScheduledAutomationSnapshot(
    Guid Id,
    string Name,
    string TaskType,
    string Frequency,
    string LocalTime,
    bool IsEnabled,
    string Status,
    string LastMessage,
    int ConsecutiveFailures,
    DateTimeOffset NextRunUtc,
    DateTimeOffset? LastRunUtc,
    DateTimeOffset? LastSuccessUtc);


public sealed record SchedulerRunHistoryRecord(
    Guid RunId,
    Guid ScheduleId,
    string SiteName,
    string ScheduleName,
    string TaskType,
    string Trigger,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string Outcome,
    string Details);
