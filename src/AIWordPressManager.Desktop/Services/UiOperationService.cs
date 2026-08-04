using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.Services;

public sealed partial class UiOperationService : ObservableObject
{
    private int _activeScopes;
    private long _updateSequence;
    private Guid? _activeOperationId;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _title = "Ready";
    [ObservableProperty] private string _step = "Idle";
    [ObservableProperty] private string _detail = "No operation is running.";
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _canRollback;
    [ObservableProperty] private string _operationType = "General";
    [ObservableProperty] private Guid? _siteId;
    [ObservableProperty] private DateTimeOffset? _startedAt;
    [ObservableProperty] private DateTimeOffset? _endedAt;
    [ObservableProperty] private string _status = "Idle";

    public ObservableCollection<UiOperationHistoryItem> History { get; } = [];
    public ObservableCollection<UiWorkflowOperation> Operations { get; } = [];

    public UiWorkflowOperation? ActiveOperation =>
        _activeOperationId is Guid id ? Operations.FirstOrDefault(x => x.Id == id) : null;

    public TimeSpan Elapsed => StartedAt is null
        ? TimeSpan.Zero
        : (EndedAt ?? DateTimeOffset.Now) - StartedAt.Value;

    public IDisposable Begin(
        string title,
        string step,
        string detail,
        int progress = 0,
        bool indeterminate = false,
        string operationType = "General",
        Guid? siteId = null,
        bool canCancel = false,
        bool canRetry = true,
        bool canRollback = false)
    {
        _activeScopes++;
        Start(title, step, detail, progress, indeterminate, operationType, siteId, canCancel, canRetry, canRollback);
        return new OperationScope(this, _activeOperationId);
    }

    public void Start(
        string title,
        string step,
        string detail,
        int progress = 0,
        bool indeterminate = false,
        string operationType = "General",
        Guid? siteId = null,
        bool canCancel = false,
        bool canRetry = true,
        bool canRollback = false)
    {
        var operation = new UiWorkflowOperation
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            OperationType = operationType,
            Title = title,
            Stage = step,
            Detail = detail,
            Percent = Math.Clamp(progress, 0, 100),
            Status = "Running",
            StartedAt = DateTimeOffset.Now,
            CanCancel = canCancel,
            CanRetry = canRetry,
            CanRollback = canRollback
        };

        RunOnUiThread(() =>
        {
            Operations.Insert(0, operation);
            while (Operations.Count > 100)
                Operations.RemoveAt(Operations.Count - 1);
        });

        _activeOperationId = operation.Id;
        OperationType = operationType;
        SiteId = siteId;
        StartedAt = operation.StartedAt;
        EndedAt = null;
        Status = "Running";
        Title = title;
        Step = step;
        Detail = detail;
        Progress = operation.Percent;
        IsIndeterminate = indeterminate;
        CanCancel = canCancel;
        CanRetry = canRetry;
        CanRollback = canRollback;
        IsRunning = true;
        OnPropertyChanged(nameof(ActiveOperation));
        OnPropertyChanged(nameof(Elapsed));
        AddHistory("Started", step, detail, Progress, operation.Id);
    }

    public void Report(int progress, string step, string? detail = null, bool? indeterminate = null)
    {
        Progress = Math.Clamp(progress, 0, 100);
        Step = step;
        if (!string.IsNullOrWhiteSpace(detail)) Detail = detail;
        if (indeterminate.HasValue) IsIndeterminate = indeterminate.Value;
        IsRunning = true;
        Status = "Running";

        UpdateActiveOperation(x =>
        {
            x.Percent = Progress;
            x.Stage = Step;
            x.Detail = Detail;
            x.Status = "Running";
        });

        OnPropertyChanged(nameof(Elapsed));
        AddHistory("Running", Step, Detail, Progress, _activeOperationId);
    }

    public void Complete(string detail = "Completed")
    {
        Progress = 100;
        Step = "Completed";
        Detail = detail;
        Status = "Completed";
        EndedAt = DateTimeOffset.Now;
        IsIndeterminate = false;
        IsRunning = true;
        CanCancel = false;

        UpdateActiveOperation(x =>
        {
            x.Percent = 100;
            x.Stage = "Completed";
            x.Detail = detail;
            x.Status = "Completed";
            x.EndedAt = EndedAt;
            x.CanCancel = false;
        });

        OnPropertyChanged(nameof(Elapsed));
        AddHistory("Completed", Step, Detail, 100, _activeOperationId);

        var completionSequence = Interlocked.Read(ref _updateSequence);
        _ = HideCompletedOperationAsync(completionSequence);
    }

    public void Fail(string detail)
    {
        Step = "Failed";
        Detail = detail;
        Status = "Failed";
        EndedAt = DateTimeOffset.Now;
        IsIndeterminate = false;
        IsRunning = true;
        CanCancel = false;

        UpdateActiveOperation(x =>
        {
            x.Stage = "Failed";
            x.Detail = detail;
            x.Status = "Failed";
            x.EndedAt = EndedAt;
            x.CanCancel = false;
        });

        OnPropertyChanged(nameof(Elapsed));
        AddHistory("Failed", Step, Detail, Progress, _activeOperationId);
    }

    public void Cancel(string detail = "Cancelled by user")
    {
        Step = "Cancelled";
        Detail = detail;
        Status = "Cancelled";
        EndedAt = DateTimeOffset.Now;
        IsIndeterminate = false;
        IsRunning = false;
        CanCancel = false;

        UpdateActiveOperation(x =>
        {
            x.Stage = "Cancelled";
            x.Detail = detail;
            x.Status = "Cancelled";
            x.EndedAt = EndedAt;
            x.CanCancel = false;
        });

        OnPropertyChanged(nameof(Elapsed));
        AddHistory("Cancelled", Step, Detail, Progress, _activeOperationId);
    }

    public void Hide()
    {
        Interlocked.Increment(ref _updateSequence);
        _activeScopes = 0;
        IsRunning = false;
        IsIndeterminate = false;
        CanCancel = false;
    }

    public void ClearHistory() => RunOnUiThread(History.Clear);

    public void ClearCompletedOperations() => RunOnUiThread(() =>
    {
        foreach (var item in Operations.Where(x => x.Status is "Completed" or "Cancelled").ToArray())
            Operations.Remove(item);
    });

    private void UpdateActiveOperation(Action<UiWorkflowOperation> update)
    {
        var operation = ActiveOperation;
        if (operation is null) return;
        RunOnUiThread(() => update(operation));
        OnPropertyChanged(nameof(ActiveOperation));
    }

    private void AddHistory(string state, string step, string detail, int progress, Guid? operationId)
    {
        Interlocked.Increment(ref _updateSequence);
        var item = new UiOperationHistoryItem(DateTime.Now, state, step, detail, progress, operationId);

        RunOnUiThread(() =>
        {
            var current = History.FirstOrDefault();
            if (current is not null &&
                current.State.Equals(item.State, StringComparison.OrdinalIgnoreCase) &&
                current.Step.Equals(item.Step, StringComparison.OrdinalIgnoreCase) &&
                current.Detail.Equals(item.Detail, StringComparison.OrdinalIgnoreCase) &&
                current.Progress == item.Progress &&
                current.OperationId == item.OperationId)
                return;

            History.Insert(0, item);
            while (History.Count > 250)
                History.RemoveAt(History.Count - 1);
        });
    }

    private async Task HideCompletedOperationAsync(long completionSequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.6));
        if (Interlocked.Read(ref _updateSequence) != completionSequence || Progress < 100 || Step != "Completed")
            return;

        RunOnUiThread(() =>
        {
            if (Interlocked.Read(ref _updateSequence) == completionSequence && Progress == 100 && Step == "Completed")
            {
                _activeScopes = 0;
                IsRunning = false;
                IsIndeterminate = false;
                CanCancel = false;
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private void EndScope(Guid? operationId)
    {
        _activeScopes = Math.Max(0, _activeScopes - 1);
        if (_activeScopes == 0 && _activeOperationId == operationId && Step != "Completed" && Step != "Failed")
            Hide();
    }

    private sealed class OperationScope(UiOperationService owner, Guid? operationId) : IDisposable
    {
        private UiOperationService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndScope(operationId);
    }
}

public sealed partial class UiWorkflowOperation : ObservableObject
{
    public Guid Id { get; init; }
    public Guid? SiteId { get; init; }
    public string OperationType { get; init; } = "General";
    public string Title { get; init; } = "Operation";
    public DateTimeOffset StartedAt { get; init; }
    public bool CanRetry { get; init; }
    public bool CanRollback { get; init; }

    [ObservableProperty] private string _stage = "Starting";
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private int _percent;
    [ObservableProperty] private string _status = "Running";
    [ObservableProperty] private DateTimeOffset? _endedAt;
    [ObservableProperty] private bool _canCancel;

    public TimeSpan Duration => (EndedAt ?? DateTimeOffset.Now) - StartedAt;
    public string DurationText => Duration.TotalHours >= 1
        ? Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
        : Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
}

public sealed record UiOperationHistoryItem(
    DateTime Timestamp,
    string State,
    string Step,
    string Detail,
    int Progress,
    Guid? OperationId = null)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string DisplayText => $"{TimeText}  •  {Progress,3}%  •  {Step}\n{Detail}";
}
