using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.Services;

public sealed partial class UiOperationService : ObservableObject
{
    private int _activeScopes;
    private long _updateSequence;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _title = "Ready";
    [ObservableProperty] private string _step = "Idle";
    [ObservableProperty] private string _detail = "No operation is running.";
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _canCancel;

    public ObservableCollection<UiOperationHistoryItem> History { get; } = [];

    public IDisposable Begin(string title, string step, string detail, int progress = 0, bool indeterminate = false)
    {
        _activeScopes++;
        Start(title, step, detail, progress, indeterminate);
        return new OperationScope(this);
    }

    public void Start(string title, string step, string detail, int progress = 0, bool indeterminate = false)
    {
        Title = title;
        Step = step;
        Detail = detail;
        Progress = Math.Clamp(progress, 0, 100);
        IsIndeterminate = indeterminate;
        IsRunning = true;
        AddHistory("Started", step, detail, Progress);
    }

    public void Report(int progress, string step, string? detail = null, bool? indeterminate = null)
    {
        Progress = Math.Clamp(progress, 0, 100);
        Step = step;
        if (!string.IsNullOrWhiteSpace(detail)) Detail = detail;
        if (indeterminate.HasValue) IsIndeterminate = indeterminate.Value;
        IsRunning = true;
        AddHistory("Running", Step, Detail, Progress);
    }

    public void Complete(string detail = "Completed")
    {
        Progress = 100;
        Step = "Completed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;
        AddHistory("Completed", Step, Detail, 100);

        var completionSequence = Interlocked.Read(ref _updateSequence);
        _ = HideCompletedOperationAsync(completionSequence);
    }

    public void Fail(string detail)
    {
        Step = "Failed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;
        AddHistory("Failed", Step, Detail, Progress);
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

    private void AddHistory(string state, string step, string detail, int progress)
    {
        Interlocked.Increment(ref _updateSequence);
        var item = new UiOperationHistoryItem(DateTime.Now, state, step, detail, progress);

        RunOnUiThread(() =>
        {
            var current = History.FirstOrDefault();
            if (current is not null &&
                current.State.Equals(item.State, StringComparison.OrdinalIgnoreCase) &&
                current.Step.Equals(item.Step, StringComparison.OrdinalIgnoreCase) &&
                current.Detail.Equals(item.Detail, StringComparison.OrdinalIgnoreCase) &&
                current.Progress == item.Progress)
                return;

            History.Insert(0, item);
            while (History.Count > 150)
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

    private void EndScope()
    {
        _activeScopes = Math.Max(0, _activeScopes - 1);
        if (_activeScopes == 0 && Step != "Completed")
            Hide();
    }

    private sealed class OperationScope(UiOperationService owner) : IDisposable
    {
        private UiOperationService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndScope();
    }
}

public sealed record UiOperationHistoryItem(
    DateTime Timestamp,
    string State,
    string Step,
    string Detail,
    int Progress)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string DisplayText => $"{TimeText}  •  {Progress,3}%  •  {Step}\n{Detail}";
}
