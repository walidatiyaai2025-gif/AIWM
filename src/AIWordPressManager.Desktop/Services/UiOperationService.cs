using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.Services;

public sealed partial class UiOperationService : ObservableObject
{
    private int _activeScopes;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _progress;
    [ObservableProperty] private string _title = "Ready";
    [ObservableProperty] private string _step = "Idle";
    [ObservableProperty] private string _detail = "No operation is running.";
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _canCancel;

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
    }

    public void Report(int progress, string step, string? detail = null, bool? indeterminate = null)
    {
        Progress = Math.Clamp(progress, 0, 100);
        Step = step;
        if (!string.IsNullOrWhiteSpace(detail)) Detail = detail;
        if (indeterminate.HasValue) IsIndeterminate = indeterminate.Value;
        IsRunning = true;
    }

    public void Complete(string detail = "Completed")
    {
        Progress = 100;
        Step = "Completed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;
    }

    public void Fail(string detail)
    {
        Step = "Failed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;
    }

    public void Hide()
    {
        _activeScopes = 0;
        IsRunning = false;
        IsIndeterminate = false;
        CanCancel = false;
    }

    private void EndScope()
    {
        _activeScopes = Math.Max(0, _activeScopes - 1);
        if (_activeScopes == 0) Hide();
    }

    private sealed class OperationScope(UiOperationService owner) : IDisposable
    {
        private UiOperationService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndScope();
    }
}
