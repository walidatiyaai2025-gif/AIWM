using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Application.Settings;
using AIWordPressManager.Automation.Visual;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.Services.Sites;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SuggestedChangesViewModel : ObservableObject
{
    private readonly ISuggestedChangeService _service;
    private readonly IApprovedChangeExecutionService _executionService;
    private readonly IDialogService _dialogs;
    private readonly SitesViewModel _sites;
    private readonly ISiteOperationGuard _siteOperationGuard;
    private readonly IApplicationSettingsService _settings;
    private readonly VisualInspectionService _visualInspection;
    private readonly UiOperationService _operations;
    public ObservableCollection<SuggestedChangeItem> Items { get; } = [];
    public ObservableCollection<SuggestedChangeItem> SelectedItems { get; } = [];
    public IAsyncRelayCommand GenerateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ApproveCommand { get; }
    public IAsyncRelayCommand RejectCommand { get; }
    public IAsyncRelayCommand ReturnToPendingCommand { get; }
    public IAsyncRelayCommand BulkApproveCommand { get; }
    public IAsyncRelayCommand BulkRejectCommand { get; }
    public IAsyncRelayCommand<SuggestedChangeItem> ApplyNowCommand { get; }
    public IAsyncRelayCommand ApplySafeSelectedCommand { get; }
    public IAsyncRelayCommand<SuggestedChangeItem> ExplainCommand { get; }
    public IAsyncRelayCommand<SuggestedChangeItem> ApproveItemCommand { get; }
    public IAsyncRelayCommand<SuggestedChangeItem> RejectItemCommand { get; }
    public IRelayCommand<SuggestedChangeItem> CopyCurrentCommand { get; }
    public IRelayCommand<SuggestedChangeItem> CopyProposedCommand { get; }
    public IAsyncRelayCommand ShowPendingCommand { get; }
    public IAsyncRelayCommand ShowApprovedCommand { get; }
    public IAsyncRelayCommand ShowRejectedCommand { get; }
    public IAsyncRelayCommand ShowAllCommand { get; }

    [ObservableProperty] private SuggestedChangeItem? _selectedItem;
    [ObservableProperty] private string _statusMessage = "Select a site, then generate proposals from local audit results.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusFilter;
    [ObservableProperty] private string _selectedPreviewTitle = "Select a proposal";
    [ObservableProperty] private string _selectedExecutionPlan = "Choose an item to preview the exact action, backup, execution, and verification stages.";
    [ObservableProperty] private string _selectedExpectedResult = "No change selected.";

    public int PendingCount => Items.Count(x => x.ApprovalStatus == "Pending");
    public int ApprovedCount => Items.Count(x => x.ApprovalStatus == "Approved");
    public int RejectedCount => Items.Count(x => x.ApprovalStatus == "Rejected");
    public int SelectedCount => SelectedItems.Count;
    public int SafeSelectedCount => SelectedItems.Count(IsSafeDirectAction);
    public int DirectlyExecutableCount => Items.Count(x => x.ApprovalStatus == "Pending" && IsSafeDirectAction(x));
    public int HighRiskCount => Items.Count(x => x.ApprovalStatus == "Pending" && x.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase));
    public int StagingCount => Items.Count(x => x.ApprovalStatus == "Pending" && x.RequiresStaging);
    public string ActiveQueueLabel => string.IsNullOrWhiteSpace(StatusFilter) ? "All proposals" : $"{StatusFilter} proposals";

    public SuggestedChangesViewModel(
        ISuggestedChangeService service,
        IApprovedChangeExecutionService executionService,
        IDialogService dialogs,
        SitesViewModel sites,
        ISiteOperationGuard siteOperationGuard,
        IApplicationSettingsService settings,
        VisualInspectionService visualInspection,
        UiOperationService operations)
    {
        _service = service;
        _executionService = executionService;
        _dialogs = dialogs;
        _sites = sites;
        _siteOperationGuard = siteOperationGuard;
        _settings = settings;
        _visualInspection = visualInspection;
        _operations = operations;
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, CanWork);
        RefreshCommand = new AsyncRelayCommand(LoadAsync, CanWork);
        ApproveCommand = new AsyncRelayCommand(() => SetStatusAsync("Approved"), () => SelectedItem is not null && !IsBusy);
        RejectCommand = new AsyncRelayCommand(() => SetStatusAsync("Rejected"), () => SelectedItem is not null && !IsBusy);
        ReturnToPendingCommand = new AsyncRelayCommand(() => SetStatusAsync("Pending"), () => SelectedItem is not null && !IsBusy);
        BulkApproveCommand = new AsyncRelayCommand(() => SetBulkStatusAsync("Approved"), () => SelectedItems.Count > 0 && !IsBusy);
        BulkRejectCommand = new AsyncRelayCommand(() => SetBulkStatusAsync("Rejected"), () => SelectedItems.Count > 0 && !IsBusy);
        ApplyNowCommand = new AsyncRelayCommand<SuggestedChangeItem>(ApplyNowAsync, item => item is not null && !IsBusy);
        ApplySafeSelectedCommand = new AsyncRelayCommand(ApplySafeSelectedAsync, () => SafeSelectedCount > 0 && !IsBusy);
        ExplainCommand = new AsyncRelayCommand<SuggestedChangeItem>(ExplainAsync, item => item is not null && !IsBusy);
        ApproveItemCommand = new AsyncRelayCommand<SuggestedChangeItem>(item => SetItemStatusAsync(item, "Approved"), item => item is not null && !IsBusy);
        RejectItemCommand = new AsyncRelayCommand<SuggestedChangeItem>(item => SetItemStatusAsync(item, "Rejected"), item => item is not null && !IsBusy);
        CopyCurrentCommand = new RelayCommand<SuggestedChangeItem>(item => CopyText(item?.CurrentValue), item => item is not null);
        CopyProposedCommand = new RelayCommand<SuggestedChangeItem>(item => CopyText(item?.ProposedValue), item => item is not null);
        ShowPendingCommand = new AsyncRelayCommand(() => SetQueueFilterAsync("Pending"), CanWork);
        ShowApprovedCommand = new AsyncRelayCommand(() => SetQueueFilterAsync("Approved"), CanWork);
        ShowRejectedCommand = new AsyncRelayCommand(() => SetQueueFilterAsync("Rejected"), CanWork);
        ShowAllCommand = new AsyncRelayCommand(() => SetQueueFilterAsync(null), CanWork);
        SelectedItems.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(SelectedCount)); OnPropertyChanged(nameof(SafeSelectedCount)); NotifyCommands(); };
        _sites.SelectedSiteChanged += (_, _) =>
        {
            Items.Clear();
            SelectedItems.Clear();
            SelectedItem = null;
            StatusMessage = _sites.SelectedSite is null
                ? "Select a site, then generate proposals from local audit results."
                : $"{_sites.SelectedSite.Name} selected. Load its isolated proposal queue.";
            RaiseCounts();
            NotifyCommands();
        };
    }

    partial void OnSelectedItemChanged(SuggestedChangeItem? value)
    {
        UpdateSelectedPreview(value);
        NotifyCommands();
    }
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    private bool CanWork() => _sites.SelectedSite is not null && !IsBusy;

    public async Task LoadAsync()
    {
        if (_sites.SelectedSite is null) { Items.Clear(); StatusMessage = "Select a site first."; return; }
        var lease = _siteOperationGuard.Begin("Loading suggested changes");
        IsBusy = true;
        using var operation = _operations.Begin(
            "Loading AI review",
            "Reading saved proposals",
            $"Loading {ActiveQueueLabel.ToLowerInvariant()} for {lease.SiteName}.",
            20);
        try
        {
            var rows = await _service.GetAsync(lease.SiteId, StatusFilter);
            _siteOperationGuard.EnsureCurrent(lease);
            _operations.Report(80, "Preparing review workspace", "Sorting proposals and refreshing counters.");
            Items.Clear(); SelectedItems.Clear(); foreach (var row in rows) Items.Add(row);
            StatusMessage = $"Loaded {Items.Count} item(s) for {lease.SiteName} in {ActiveQueueLabel.ToLowerInvariant()}. Approval changes workflow state only; WordPress is not modified here.";
            RaiseCounts();
            OnPropertyChanged(nameof(ActiveQueueLabel));
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task ShowApprovalQueueAsync() { StatusFilter = "Pending"; await LoadAsync(); }
    public async Task ShowAllAsync() { StatusFilter = null; await LoadAsync(); }

    private async Task SetQueueFilterAsync(string? status)
    {
        StatusFilter = status;
        SelectedItem = null;
        SelectedItems.Clear();
        OnPropertyChanged(nameof(ActiveQueueLabel));
        await LoadAsync();
    }

    private async Task GenerateAsync()
    {
        if (_sites.SelectedSite is null) return;
        var lease = _siteOperationGuard.Begin("Generating suggested changes");
        IsBusy = true;
        using var operation = _operations.Begin(
            "Generating AI review",
            "Reading audit findings",
            $"Building safe, explainable proposals for {lease.SiteName}. Navigation is locked until this step finishes.",
            10);
        try
        {
            _operations.Report(35, "Building proposals", "Converting SEO, content, link, and visual findings into actionable changes.");
            var result = await _service.GenerateFromLocalInsightsAsync(lease.SiteId);
            _siteOperationGuard.EnsureCurrent(lease);
            _operations.Report(70, "Applying automation policy", "Evaluating risk, approval, evidence, and verification requirements.");
            StatusMessage = $"Generated {result.Created} new proposals; {result.Existing} already existed; {result.TotalPending} await review.";
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = ex.Message;
            return;
        }
        finally { IsBusy = false; }
        await LoadAsync();
        _siteOperationGuard.EnsureCurrent(lease);

        var automation = await _settings.GetAiAutomationSettingsAsync();
        _siteOperationGuard.EnsureCurrent(lease);
        if (automation.AutoRejectHighRiskAiActions)
        {
            foreach (var highRisk in Items.Where(x => x.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase) && x.ApprovalStatus == "Pending").ToArray())
            {
                _siteOperationGuard.EnsureCurrent(lease);
                await _service.SetApprovalStatusAsync(highRisk.Id, "Rejected");
            }
        }
        if (IsAutomaticExecutionEnabled(automation))
        {
            var safe = Items.Where(IsSafeDirectAction).ToArray();
            if (safe.Length > 0) await ExecuteAutomaticallyAsync(lease, safe, automation);
        }
        else if (automation.AutoExecuteLowRiskAiActions)
        {
            StatusMessage = "AI automation did not execute because verification, evidence capture, high-risk rejection, or AutoLowRisk policy is not fully enabled.";
        }
        await LoadAsync();
    }

    private async Task SetStatusAsync(string status)
    {
        if (SelectedItem is null) return;
        var lease = _siteOperationGuard.Begin($"Marking a proposal as {status}");
        IsBusy = true;
        try
        {
            _siteOperationGuard.EnsureCurrent(lease);
            await _service.SetApprovalStatusAsync(SelectedItem.Id, status);
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = $"Change marked as {status} for {lease.SiteName}. No WordPress operation was executed.";
        }
        catch (OperationCanceledException ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
        await LoadAsync();
    }

    private async Task SetItemStatusAsync(SuggestedChangeItem? item, string status)
    {
        if (item is null) return;
        var lease = _siteOperationGuard.Begin($"Marking a proposal as {status}");
        SelectedItem = item;
        IsBusy = true;
        try
        {
            _siteOperationGuard.EnsureCurrent(lease);
            await _service.SetApprovalStatusAsync(item.Id, status);
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = $"Change marked as {status} for {lease.SiteName}. No WordPress operation was executed.";
        }
        catch (OperationCanceledException ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
        await LoadAsync();
    }

    private async Task ApplyNowAsync(SuggestedChangeItem? item)
    {
        if (_sites.SelectedSite is null || item is null) return;
        var lease = _siteOperationGuard.Begin("Applying an AI suggestion");

        if (!item.CanApplyDirectly)
        {
            StatusMessage = $"{item.ChangeType} is not directly executable. The item is selected and ready for approval or routing to its specialist editor.";
            var specialistMessage = string.Join(
                Environment.NewLine,
                $"This {item.ChangeType} result is valid, but its adapter is not allowed to write directly to WordPress yet.",
                "You can approve it now, then complete it from the related editor or staging workflow.",
                string.Empty,
                $"Reason: {item.CleanReason}");

            await _dialogs.ShowInformationAsync("Specialist workflow required", specialistMessage);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Apply AI suggestion",
            $"Apply this {item.ChangeType} suggestion to {lease.SiteName} now?\n\nCurrent:\n{item.CurrentValue}\n\nAI suggestion ({item.AiProvider}):\n{item.ProposedValue}\n\nThe change will be approved, backed up, sent to WordPress, and verified.");
        if (!confirmed) return;
        _siteOperationGuard.EnsureCurrent(lease);

        IsBusy = true;
        using var operation = _operations.Begin(
            "Executing approved WordPress change",
            "Preparing safety controls",
            $"Creating evidence and executing {item.ChangeType} for {lease.SiteName}. The application is locked to preserve execution context.",
            5);
        try
        {
            var automation = await _settings.GetAiAutomationSettingsAsync();
            _siteOperationGuard.EnsureCurrent(lease);
            _operations.Report(20, "Capturing before evidence", "Preparing the current-state evidence and execution backup.");
            if (automation.CaptureBeforeAfterEvidence) await CaptureEvidenceAsync(lease.SiteUrl, "before", lease);
            _siteOperationGuard.EnsureCurrent(lease);
            await _service.SetApprovalStatusAsync(item.Id, "Approved");
            _siteOperationGuard.EnsureCurrent(lease);
            var progress = new Progress<(int Percent, string Step)>(p =>
            {
                if (_siteOperationGuard.IsCurrent(lease)) StatusMessage = $"{p.Percent}% — {p.Step}";
            });
            var result = await _executionService.ExecuteAsync(lease.SiteId, [item.Id], progress);
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = result.IsSuccess
                ? $"Applied and verified {result.Value.Verified} change(s). Failed: {result.Value.Failed}; skipped: {result.Value.Skipped}."
                : result.Error.Message;

            if (result.IsSuccess && result.Value.Verified > 0 && automation.CaptureBeforeAfterEvidence)
                await CaptureEvidenceAsync(lease.SiteUrl, "after", lease);

            _siteOperationGuard.EnsureCurrent(lease);
            if (result.IsFailure || result.Value.Verified == 0)
                await _dialogs.ShowErrorAsync("Suggestion was not applied", StatusMessage);
            else
                await _dialogs.ShowInformationAsync("Suggestion applied", $"The {item.ChangeType} suggestion from {item.AiProvider} was applied and verified successfully for {lease.SiteName}.");
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = ex.Message;
            await _dialogs.ShowErrorAsync("Execution cancelled safely", ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (_siteOperationGuard.IsCurrent(lease)) await LoadAsync();
        }
    }

    private async Task ApplySafeSelectedAsync()
    {
        if (_sites.SelectedSite is null) return;
        var lease = _siteOperationGuard.Begin("Applying selected safe suggestions");
        var safe = SelectedItems.Where(IsSafeDirectAction).ToArray();
        if (safe.Length == 0) return;

        var summary = string.Join("\n", safe.Take(8).Select(x => $"• {x.ObjectType} {x.ObjectId}: {x.ChangeType}"));
        if (safe.Length > 8) summary += $"\n• …and {safe.Length - 8} more";
        var confirmed = await _dialogs.ConfirmAsync(
            "Apply safe selected changes",
            $"Apply {safe.Length} low-risk direct changes to {lease.SiteName}?\n\n{summary}\n\nThe application will approve them, create a backup, execute them on WordPress, and verify the saved values.");
        if (!confirmed) return;
        _siteOperationGuard.EnsureCurrent(lease);

        IsBusy = true;
        try
        {
            foreach (var item in safe)
            {
                _siteOperationGuard.EnsureCurrent(lease);
                if (!item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                    await _service.SetApprovalStatusAsync(item.Id, "Approved");
            }

            _siteOperationGuard.EnsureCurrent(lease);
            var progress = new Progress<(int Percent, string Step)>(p =>
            {
                if (_siteOperationGuard.IsCurrent(lease)) StatusMessage = $"{p.Percent}% — {p.Step}";
            });
            var result = await _executionService.ExecuteAsync(lease.SiteId, safe.Select(x => x.Id).ToArray(), progress);
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = result.IsSuccess
                ? $"Safe batch completed for {lease.SiteName}. Verified: {result.Value.Verified}; failed: {result.Value.Failed}; skipped: {result.Value.Skipped}."
                : result.Error.Message;

            if (result.IsFailure || result.Value.Verified == 0)
                await _dialogs.ShowErrorAsync("Safe batch was not completed", StatusMessage);
            else
                await _dialogs.ShowInformationAsync("Safe batch completed", StatusMessage);
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = ex.Message;
            await _dialogs.ShowErrorAsync("Batch cancelled safely", ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (_siteOperationGuard.IsCurrent(lease)) await LoadAsync();
        }
    }

    private async Task ExecuteAutomaticallyAsync(SiteOperationLease lease, IReadOnlyCollection<SuggestedChangeItem> safe, AiAutomationSettings automation)
    {
        IsBusy = true;
        try
        {
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = $"AI policy approved {safe.Count} low-risk action(s) for {lease.SiteName}. Capturing evidence and executing now…";
            if (automation.CaptureBeforeAfterEvidence) await CaptureEvidenceAsync(lease.SiteUrl, "before-auto", lease);
            foreach (var item in safe)
            {
                _siteOperationGuard.EnsureCurrent(lease);
                await _service.SetApprovalStatusAsync(item.Id, "Approved");
            }
            _siteOperationGuard.EnsureCurrent(lease);
            var result = await _executionService.ExecuteAsync(
                lease.SiteId,
                safe.Select(x => x.Id).ToArray(),
                new Progress<(int Percent, string Step)>(p =>
                {
                    if (_siteOperationGuard.IsCurrent(lease)) StatusMessage = $"{p.Percent}% — {p.Step}";
                }));
            _siteOperationGuard.EnsureCurrent(lease);
            if (result.IsSuccess && result.Value.Verified > 0)
            {
                if (automation.CaptureBeforeAfterEvidence) await CaptureEvidenceAsync(lease.SiteUrl, "after-auto", lease);
                StatusMessage = $"AI execution completed for {lease.SiteName} with verified results: {result.Value.Verified}; failed: {result.Value.Failed}; skipped: {result.Value.Skipped}.";
            }
            else StatusMessage = result.IsFailure ? result.Error.Message : "AI execution did not produce a verified WordPress result.";
        }
        catch (OperationCanceledException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Automatic AI execution stopped safely: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task CaptureEvidenceAsync(string siteUrl, string stage, SiteOperationLease lease)
    {
        try
        {
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = $"Capturing {stage} website evidence…";
            await _visualInspection.InspectAsync(siteUrl, new Progress<VisualInspectionProgress>(p =>
            {
                if (_siteOperationGuard.IsCurrent(lease)) StatusMessage = $"{p.Percent}% — {stage}: {p.Step}";
            }));
            _siteOperationGuard.EnsureCurrent(lease);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Execution can continue, but {stage} screenshot evidence was unavailable: {ex.Message}";
        }
    }

    private static bool IsAutomaticExecutionEnabled(AiAutomationSettings settings) =>
        settings.AutoExecuteLowRiskAiActions &&
        settings.ErrorDecisionMode.Equals("AutoLowRisk", StringComparison.OrdinalIgnoreCase) &&
        settings.AutoRejectHighRiskAiActions &&
        settings.CaptureBeforeAfterEvidence &&
        settings.RequireVerifiedExecutionResult;

    private static bool IsSafeDirectAction(SuggestedChangeItem item) =>
        item.CanApplyDirectly &&
        item.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase) &&
        !item.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase);

    private void UpdateSelectedPreview(SuggestedChangeItem? item)
    {
        if (item is null)
        {
            SelectedPreviewTitle = "Select a proposal";
            SelectedExecutionPlan = "Choose an item to preview the exact action, backup, execution, and verification stages.";
            SelectedExpectedResult = "No change selected.";
            return;
        }

        SelectedPreviewTitle = $"{item.ObjectType} • {item.ChangeType}";
        SelectedExecutionPlan = item.CanApplyDirectly
            ? $"1. Approve proposal\n2. Create local backup\n3. Send {item.ChangeType} to WordPress\n4. Read the object again\n5. Verify the saved value"
            : "This proposal requires a specialist editor, staging, or manual review before execution.";
        SelectedExpectedResult = item.CanApplyDirectly
            ? $"Expected verified value:\n{item.ProposedValue}"
            : "The proposal will remain in the approval workflow and will not be sent directly to WordPress.";
    }

    private static void CopyText(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            System.Windows.Clipboard.SetText(value);
    }

    private Task ExplainAsync(SuggestedChangeItem? item)
    {
        if (item is null) return Task.CompletedTask;
        var message = $"AI provider: {item.AiProvider}\nConfidence: {item.ConfidenceDisplay}\nRisk: {item.RiskLevel}\n\nWhy this was suggested:\n{item.CleanReason}\n\nExact proposed value/action:\n{item.ProposedValue}";
        return _dialogs.ShowInformationAsync("AI suggestion details", message);
    }

    private async Task SetBulkStatusAsync(string status)
    {
        var selected = SelectedItems.ToArray();
        if (selected.Length == 0) return;
        var lease = _siteOperationGuard.Begin($"Marking selected proposals as {status}");

        var riskSummary = $"Low: {selected.Count(x => x.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase))}, " +
                          $"Medium: {selected.Count(x => x.RiskLevel.Equals("Medium", StringComparison.OrdinalIgnoreCase))}, " +
                          $"High: {selected.Count(x => x.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase))}";
        var confirmed = await _dialogs.ConfirmAsync(
            $"{status} selected proposals",
            $"Mark {selected.Length} selected proposal(s) for {lease.SiteName} as {status}?\n\n{riskSummary}\n\nThis changes approval state only and does not write to WordPress.");
        if (!confirmed) return;
        _siteOperationGuard.EnsureCurrent(lease);

        IsBusy = true;
        try
        {
            foreach (var item in selected)
            {
                _siteOperationGuard.EnsureCurrent(lease);
                await _service.SetApprovalStatusAsync(item.Id, status);
            }
            _siteOperationGuard.EnsureCurrent(lease);
            StatusMessage = $"{selected.Length} changes marked as {status} for {lease.SiteName}. No WordPress operation was executed.";
        }
        catch (OperationCanceledException ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
        await LoadAsync();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ApprovedCount));
        OnPropertyChanged(nameof(RejectedCount));
        OnPropertyChanged(nameof(DirectlyExecutableCount));
        OnPropertyChanged(nameof(HighRiskCount));
        OnPropertyChanged(nameof(StagingCount));
    }
    private void NotifyCommands() { GenerateCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged(); ApproveCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged(); ReturnToPendingCommand.NotifyCanExecuteChanged(); BulkApproveCommand.NotifyCanExecuteChanged(); BulkRejectCommand.NotifyCanExecuteChanged(); ApplyNowCommand.NotifyCanExecuteChanged(); ApplySafeSelectedCommand.NotifyCanExecuteChanged(); ExplainCommand.NotifyCanExecuteChanged(); ApproveItemCommand.NotifyCanExecuteChanged(); RejectItemCommand.NotifyCanExecuteChanged(); CopyCurrentCommand.NotifyCanExecuteChanged(); CopyProposedCommand.NotifyCanExecuteChanged(); ShowPendingCommand.NotifyCanExecuteChanged(); ShowApprovedCommand.NotifyCanExecuteChanged(); ShowRejectedCommand.NotifyCanExecuteChanged(); ShowAllCommand.NotifyCanExecuteChanged(); }
}
