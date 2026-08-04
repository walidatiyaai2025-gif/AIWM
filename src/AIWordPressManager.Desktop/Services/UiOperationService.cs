using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.Services;

public sealed partial class UiOperationService : ObservableObject
{
    private int _activeScopes;
    private int _operationVersion;
    private CancellationTokenSource? _autoHideCancellation;

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
        CancelPendingAutoHide();
        _operationVersion++;
        Title = title;
        Step = step;
        Detail = detail;
        Progress = Math.Clamp(progress, 0, 100);
        IsIndeterminate = indeterminate;
        IsRunning = true;
    }

    public void Report(int progress, string step, string? detail = null, bool? indeterminate = null)
    {
        CancelPendingAutoHide();
        _operationVersion++;
        Progress = Math.Clamp(progress, 0, 100);
        Step = step;
        if (!string.IsNullOrWhiteSpace(detail)) Detail = detail;
        if (indeterminate.HasValue) IsIndeterminate = indeterminate.Value;
        IsRunning = true;
    }

    public void Complete(string detail = "Completed")
    {
        CancelPendingAutoHide();
        Progress = 100;
        Step = "Completed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;

        var completionVersion = ++_operationVersion;
        _autoHideCancellation = new CancellationTokenSource();
        _ = AutoHideAfterCompletionAsync(completionVersion, _autoHideCancellation.Token);
    }

    public void Fail(string detail)
    {
        CancelPendingAutoHide();
        _operationVersion++;
        Step = "Failed";
        Detail = detail;
        IsIndeterminate = false;
        IsRunning = true;
    }

    public void Hide()
    {
        CancelPendingAutoHide();
        _activeScopes = 0;
        IsRunning = false;
        IsIndeterminate = false;
        CanCancel = false;
    }

    private async Task AutoHideAfterCompletionAsync(int completionVersion, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(900), cancellationToken);
            if (completionVersion != _operationVersion || !IsRunning || Progress < 100)
            {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Hide();
                return;
            }

            await dispatcher.InvokeAsync(Hide);
        }
        catch (OperationCanceledException)
        {
            // A new operation started before the completion overlay was closed.
        }
    }

    private void CancelPendingAutoHide()
    {
        var cancellation = Interlocked.Exchange(ref _autoHideCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void EndScope()
    {
        _activeScopes = Math.Max(0, _activeScopes - 1);
        if (_activeScopes == 0 && !string.Equals(Step, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            Hide();
        }
    }

    private sealed class OperationScope(UiOperationService owner) : IDisposable
    {
        private UiOperationService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndScope();
    }
}
