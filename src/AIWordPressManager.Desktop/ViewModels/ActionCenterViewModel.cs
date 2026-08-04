using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ActionCenterViewModel : ObservableObject
{
    private readonly ISuggestedChangeService _suggestions;
    private readonly IApprovedChangeExecutionService _execution;
    private readonly IDialogService _dialogs;
    private readonly SitesViewModel _sites;
    private CancellationTokenSource? _cts;

    public ObservableCollection<ActionCenterItem> Items { get; } = [];
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ApplyAllSafeCommand { get; }
    public IAsyncRelayCommand RetryFailedCommand { get; }
    public IAsyncRelayCommand RollbackSelectedCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenSuggestionsCommand { get; }
    public IRelayCommand OpenExecutionCommand { get; }

    public event Action<string>? NavigationRequested;

    [ObservableProperty] private ActionCenterItem? _selectedItem;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _currentStep = "Load the latest local action state from SQLite.";
    [ObservableProperty] private string _statusMessage = "Action Center combines safe fixes, approvals, manual work, execution results, and rollback state.";

    public int SafeCount => Items.Count(x => x.Bucket == "Safe");
    public int ApprovalCount => Items.Count(x => x.Bucket == "Approval");
    public int ManualCount => Items.Count(x => x.Bucket == "Manual");
    public int CompletedCount => Items.Count(x => x.Bucket == "Completed");
    public int FailedCount => Items.Count(x => x.Bucket == "Failed");
    public int RunningCount => Items.Count(x => x.ExecutionStatus.Equals("Executing", StringComparison.OrdinalIgnoreCase));

    public ActionCenterViewModel(
        ISuggestedChangeService suggestions,
        IApprovedChangeExecutionService execution,
        IDialogService dialogs,
        SitesViewModel sites)
    {
        _suggestions = suggestions;
        _execution = execution;
        _dialogs = dialogs;
        _sites = sites;

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ApplyAllSafeCommand = new AsyncRelayCommand(ApplyAllSafeAsync, () => SafeCount > 0 && !IsBusy);
        RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, () => FailedCount > 0 && !IsBusy);
        RollbackSelectedCommand = new AsyncRelayCommand(RollbackSelectedAsync, () => SelectedItem?.CanRollback == true && !IsBusy);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        OpenSuggestionsCommand = new RelayCommand(() => NavigationRequested?.Invoke("Suggested Changes"));
        OpenExecutionCommand = new RelayCommand(() => NavigationRequested?.Invoke("Execution Center"));
        _sites.SelectedSiteChanged += (_, _) => NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedItemChanged(ActionCenterItem? value) => NotifyCommands();

    public async Task LoadAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            Items.Clear();
            StatusMessage = "Select a site first.";
            RaiseCounts();
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _suggestions.GetAsync(site.Id);
            Items.Clear();
            foreach (var row in rows.OrderByDescending(x => x.CreatedAtUtc))
                Items.Add(ActionCenterItem.From(row));

            StatusMessage = $"Loaded {Items.Count} actions from SQLite: {SafeCount} safe, {ApprovalCount} awaiting approval, {ManualCount} manual, {CompletedCount} completed, {FailedCount} failed.";
            RaiseCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyAllSafeAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;

        var safe = Items.Where(x => x.Bucket == "Safe").ToArray();
        if (safe.Length == 0) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Apply all safe actions",
            $"Apply {safe.Length} low-risk actions now?\n\nThe application will approve them, create a backup, send the changes to WordPress, read the values again, and verify the result.");
        if (!confirmed) return;

        foreach (var item in safe)
            if (!item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                await _suggestions.SetApprovalStatusAsync(item.Id, "Approved");

        await RunBatchAsync(site.Id, safe.Select(x => x.Id).ToArray(), rollback: false, "Safe action batch");
    }

    private async Task RetryFailedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;

        var failed = Items.Where(x => x.Bucket == "Failed" && x.CanExecute).ToArray();
        if (failed.Length == 0)
        {
            await _dialogs.ShowInformationAsync("No retryable actions", "The failed actions currently require manual review or a specialist editor.");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Retry failed actions",
            $"Retry {failed.Length} failed direct action(s)? A new backup and verification pass will be created.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, failed.Select(x => x.Id).ToArray(), rollback: false, "Retry failed actions");
    }

    private async Task RollbackSelectedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null || SelectedItem?.CanRollback != true) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Rollback selected action",
            $"Restore the value saved before {SelectedItem.ChangeType} for {SelectedItem.ObjectLabel}? A fresh backup will be created first.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, [SelectedItem.Id], rollback: true, "Rollback");
    }

    private async Task RunBatchAsync(Guid siteId, Guid[] ids, bool rollback, string title)
    {
        IsBusy = true;
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<(int Percent, string Step)>(value =>
            {
                ProgressPercent = value.Percent;
                CurrentStep = value.Step;
            });

            var result = rollback
                ? await _execution.RollbackAsync(siteId, ids, progress, _cts.Token)
                : await _execution.ExecuteAsync(siteId, ids, progress, _cts.Token);

            StatusMessage = result.IsSuccess
                ? $"{title} finished. Requested: {result.Value.Requested}; verified: {result.Value.Verified}; failed: {result.Value.Failed}; skipped: {result.Value.Skipped}."
                : result.Error.Message;

            if (result.IsFailure || result.Value.Failed > 0)
                await _dialogs.ShowErrorAsync($"{title} needs attention", StatusMessage);
            else
                await _dialogs.ShowInformationAsync($"{title} completed", StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "The action batch was cancelled. Completed changes remain recorded in SQLite.";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            await LoadAsync();
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(SafeCount));
        OnPropertyChanged(nameof(ApprovalCount));
        OnPropertyChanged(nameof(ManualCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(RunningCount));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ApplyAllSafeCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        RollbackSelectedCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}

public sealed record ActionCenterItem(
    Guid Id,
    string Bucket,
    string Source,
    string ObjectLabel,
    string ChangeType,
    string Proposal,
    string RiskLevel,
    string ApprovalStatus,
    string ExecutionStatus,
    double Confidence,
    bool CanExecute,
    bool CanRollback,
    DateTime CreatedAtUtc)
{
    public string ConfidenceDisplay => $"{Confidence:P0}";

    public static ActionCenterItem From(SuggestedChangeItem item)
    {
        var executed = item.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase);
        var failed = item.ExecutionStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase);
        var safe = item.CanApplyDirectly && item.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase) && !executed;
        var bucket = executed ? "Completed"
            : failed ? "Failed"
            : safe ? "Safe"
            : item.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "Approval"
            : "Manual";

        return new ActionCenterItem(
            item.Id,
            bucket,
            item.AiProvider,
            $"{item.ObjectType} {item.ObjectId}",
            item.ChangeType,
            item.ProposedValue,
            item.RiskLevel,
            item.ApprovalStatus,
            item.ExecutionStatus,
            item.Confidence,
            item.CanApplyDirectly,
            executed,
            item.CreatedAtUtc);
    }
}
