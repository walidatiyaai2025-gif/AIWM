using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Application.Settings;
using AIWordPressManager.Automation.Visual;
using System.Diagnostics;
using System.IO;
using AIWordPressManager.Desktop.ViewModels.Sites;
using AIWordPressManager.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ExecutionCenterViewModel : ObservableObject
{
    private readonly IApprovedChangeExecutionService _service;
    private readonly ISuggestedChangeService _changes;
    private readonly SitesViewModel _sites;
    private readonly IDialogService _dialogs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobCancellationRegistry _cancellationRegistry;
    private readonly IApplicationSettingsService _settings;
    private readonly VisualInspectionService _visualInspection;
    private readonly UiOperationService _operations;
    private CancellationTokenSource? _cts;
    private IDisposable? _jobRegistration;

    public ObservableCollection<ApprovedChangeExecutionItem> Items { get; } = [];
    public ObservableCollection<ApprovedChangeExecutionItem> SelectedItems { get; } = [];
    public ObservableCollection<ExecutionPipelineStep> PipelineSteps { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ApproveSelectedCommand { get; }
    public IAsyncRelayCommand ApproveAllLowRiskCommand { get; }
    public IAsyncRelayCommand PrepareSelectedCommand { get; }
    public IAsyncRelayCommand PrepareAllSupportedCommand { get; }
    public IAsyncRelayCommand CompleteAndExecuteSelectedCommand { get; }
    public IRelayCommand GoToFirstExecutableCommand { get; }
    public IRelayCommand BuildPlanCommand { get; }
    public IAsyncRelayCommand RunSafePlanCommand { get; }
    public IRelayCommand SelectReadyCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IAsyncRelayCommand ExecuteSelectedCommand { get; }
    public IAsyncRelayCommand ExecuteAllReadyCommand { get; }
    public IAsyncRelayCommand RetryFailedCommand { get; }
    public IAsyncRelayCommand RollbackSelectedCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenBeforeEvidenceCommand { get; }
    public IRelayCommand OpenAfterEvidenceCommand { get; }

    [ObservableProperty] private ApprovedChangeExecutionItem? _selectedItem;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _currentStep = "Load approved changes from the offline database.";
    [ObservableProperty] private string _statusMessage = "Execution Center is ready. Approved changes are validated, backed up, executed, and verified.";
    [ObservableProperty] private Guid? _currentJobId;
    [ObservableProperty] private string _queueState = "Idle";
    [ObservableProperty] private DateTime? _lastExecutionUtc;
    [ObservableProperty] private int _planSafeCount;
    [ObservableProperty] private int _planReviewCount;
    [ObservableProperty] private int _planManualCount;
    [ObservableProperty] private string _planSummary = "Build a plan to rank the current queue before execution.";
    [ObservableProperty] private string? _beforeEvidencePath;
    [ObservableProperty] private string? _afterEvidencePath;
    [ObservableProperty] private string _evidenceStatus = "Before/after evidence will appear here when screenshot capture is enabled.";

    public int SelectedCount => SelectedItems.Count;
    public int PendingApprovalCount => Items.Count(x => x.ApprovalStatus == "Pending");
    public int ReadyCount => Items.Count(x => x.CanExecute && x.ExecutionStatus != "Executed");
    public int ExecutedCount => Items.Count(x => x.ExecutionStatus == "Executed");
    public int FailedCount => Items.Count(x => x.ExecutionStatus == "Failed");
    public int BlockedCount => Items.Count(x => !x.CanExecute);
    public int NeedsCompletionCount => Items.Count(NeedsExecutableValue);
    public string LastExecutionText => LastExecutionUtc is null ? "No execution in this session" : $"Last activity {LastExecutionUtc.Value.ToLocalTime():g}";

    public ExecutionCenterViewModel(
        IApprovedChangeExecutionService service,
        ISuggestedChangeService changes,
        SitesViewModel sites,
        IDialogService dialogs,
        IServiceScopeFactory scopeFactory,
        IJobCancellationRegistry cancellationRegistry,
        IApplicationSettingsService settings,
        VisualInspectionService visualInspection,
        UiOperationService operations)
    {
        _service = service;
        _changes = changes;
        _sites = sites;
        _dialogs = dialogs;
        _scopeFactory = scopeFactory;
        _cancellationRegistry = cancellationRegistry;
        _settings = settings;
        _visualInspection = visualInspection;
        _operations = operations;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        // Keep action buttons clickable whenever the center is idle. Each action explains why no row qualifies,
        // instead of looking broken because CanExecute evaluated before DataGrid selection finished updating.
        ApproveSelectedCommand = new AsyncRelayCommand(ApproveSelectedAsync, () => !IsBusy);
        ApproveAllLowRiskCommand = new AsyncRelayCommand(ApproveAllLowRiskAsync, () => !IsBusy);
        PrepareSelectedCommand = new AsyncRelayCommand(PrepareSelectedAsync, () => !IsBusy);
        PrepareAllSupportedCommand = new AsyncRelayCommand(PrepareAllSupportedAsync, () => !IsBusy);
        CompleteAndExecuteSelectedCommand = new AsyncRelayCommand(CompleteAndExecuteSelectedAsync, () => !IsBusy);
        GoToFirstExecutableCommand = new RelayCommand(GoToFirstExecutable, () => !IsBusy);
        BuildPlanCommand = new RelayCommand(BuildPlan, () => !IsBusy);
        RunSafePlanCommand = new AsyncRelayCommand(RunSafePlanAsync, () => !IsBusy);
        SelectReadyCommand = new RelayCommand(SelectReady, () => !IsBusy);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy);
        ExecuteSelectedCommand = new AsyncRelayCommand(ExecuteSelectedAsync, () => !IsBusy);
        ExecuteAllReadyCommand = new AsyncRelayCommand(ExecuteAllReadyAsync, () => !IsBusy);
        RetryFailedCommand = new AsyncRelayCommand(RetryFailedAsync, () => !IsBusy);
        RollbackSelectedCommand = new AsyncRelayCommand(RollbackSelectedAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelCurrent, () => IsBusy);
        OpenBeforeEvidenceCommand = new RelayCommand(() => OpenEvidence(BeforeEvidencePath), () => !string.IsNullOrWhiteSpace(BeforeEvidencePath));
        OpenAfterEvidenceCommand = new RelayCommand(() => OpenEvidence(AfterEvidencePath), () => !string.IsNullOrWhiteSpace(AfterEvidencePath));

        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            NotifyCommands();
        };
        _sites.SelectedSiteChanged += (_, _) => NotifyCommands();
        BuildPreviewPipeline(null);
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnSelectedItemChanged(ApprovedChangeExecutionItem? value)
    {
        BuildPreviewPipeline(value);
        NotifyCommands();
    }
    partial void OnBeforeEvidencePathChanged(string? value) => OpenBeforeEvidenceCommand.NotifyCanExecuteChanged();
    partial void OnAfterEvidencePathChanged(string? value) => OpenAfterEvidenceCommand.NotifyCanExecuteChanged();
    partial void OnLastExecutionUtcChanged(DateTime? value) => OnPropertyChanged(nameof(LastExecutionText));

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
        QueueState = "Loading";
        ProgressPercent = 10;
        CurrentStep = "Reading approved changes from SQLite";
        using var operation = _operations.Begin(
            "Loading execution center",
            "Reading approved changes",
            $"Preparing the safe execution workspace for {site.Name}. Navigation is locked until loading finishes.",
            10);
        try
        {
            var rows = await _service.GetApprovedQueueAsync(site.Id);
            _operations.Report(75, "Building execution plan", "Evaluating approvals, adapters, risk, evidence, and verification readiness.");
            Items.Clear();
            SelectedItems.Clear();
            foreach (var row in rows) Items.Add(row);
            ProgressPercent = 100;
            QueueState = "Idle";
            StatusMessage = $"Loaded {Items.Count} changes: {PendingApprovalCount} pending approval, {ReadyCount} ready, {ExecutedCount} executed, {FailedCount} failed, {BlockedCount} blocked/manual.";
            BuildPlan();
            RaiseCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyCollection<ApprovedChangeExecutionItem> SelectedCandidates()
    {
        if (SelectedItems.Count > 0)
            return SelectedItems.ToArray();
        return SelectedItem is null ? Array.Empty<ApprovedChangeExecutionItem>() : new[] { SelectedItem };
    }

    private async Task ApproveSelectedAsync()
    {
        var pending = SelectedCandidates().Where(x => x.CanApprove).ToArray();
        if (pending.Length == 0)
        {
            StatusMessage = "Select one or more pending rows first. Approval is allowed even when direct execution requires a manual adapter or staging.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Approve selected changes",
            $"Approve {pending.Length} selected concrete change(s)? They will become available for execution, but will not be applied until you press Execute.");
        if (!confirmed) return;

        IsBusy = true;
        QueueState = "Approving";
        ProgressPercent = 0;
        using var operation = _operations.Begin(
            "Approving selected changes",
            "Updating approval workflow",
            $"Approving {pending.Length} selected change(s). WordPress will not be modified in this step.",
            5);
        try
        {
            for (var index = 0; index < pending.Length; index++)
            {
                CurrentStep = $"Approving {index + 1} of {pending.Length}";
                ProgressPercent = (index * 100) / pending.Length;
                _operations.Report(ProgressPercent, CurrentStep, $"Updating approval {index + 1} of {pending.Length}.");
                await _changes.SetApprovalStatusAsync(pending[index].ChangeId, "Approved");
            }
            ProgressPercent = 100;
            QueueState = "Approved";
            StatusMessage = $"Approved {pending.Length} change(s). Select them and execute when ready.";
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private async Task ApproveAllLowRiskAsync()
    {
        var pending = Items.Where(x => x.CanApprove && x.RiskLevel == "Low" && !x.RequiresStaging).ToArray();
        if (pending.Length == 0)
        {
            StatusMessage = "There are no pending low-risk changes to approve.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Approve all low-risk changes",
            $"Approve {pending.Length} low-risk proposal(s)? Unsupported items may be approved for tracking but will remain blocked from execution.");
        if (!confirmed) return;

        IsBusy = true;
        QueueState = "Approving safe plan";
        using var operation = _operations.Begin(
            "Approving safe plan",
            "Applying low-risk approval policy",
            $"Approving {pending.Length} low-risk proposal(s) without writing to WordPress.",
            5);
        try
        {
            for (var index = 0; index < pending.Length; index++)
            {
                CurrentStep = $"Approving safe proposal {index + 1} of {pending.Length}";
                ProgressPercent = ((index + 1) * 100) / pending.Length;
                _operations.Report(ProgressPercent, CurrentStep, $"Approving safe proposal {index + 1} of {pending.Length}.");
                await _changes.SetApprovalStatusAsync(pending[index].ChangeId, "Approved");
            }
            StatusMessage = $"Approved {pending.Length} low-risk proposal(s).";
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private async Task PrepareSelectedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var selected = SelectedCandidates().Where(NeedsExecutableValue).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "The selected rows already contain executable values or require a manual editor that cannot be normalized safely.";
            await _dialogs.ShowInformationAsync("Nothing to prepare", StatusMessage);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Prepare executable values",
            $"Convert {selected.Length} selected audit result(s) into concrete WordPress actions? Supported SEO title, slug, and description findings will receive exact values and return to Pending for review.");
        if (!confirmed) return;

        IsBusy = true;
        QueueState = "Preparing values";
        ProgressPercent = 20;
        CurrentStep = "Normalizing audit findings into executable actions";
        using var operation = _operations.Begin(
            "Preparing executable values",
            CurrentStep,
            $"Converting {selected.Length} selected finding(s) into concrete, reviewable WordPress actions.",
            20);
        try
        {
            var result = await _service.PrepareExecutableValuesAsync(site.Id, selected.Select(x => x.ChangeId).ToArray());
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            ProgressPercent = 100;
            var value = result.Value;
            StatusMessage = $"Prepared {value.Prepared}; already executable {value.AlreadyExecutable}; unsupported/manual {value.Unsupported}. Review the new values before approval.";
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private async Task PrepareAllSupportedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var candidates = Items.Where(NeedsExecutableValue).Select(x => x.ChangeId).Distinct().ToArray();
        if (candidates.Length == 0)
        {
            StatusMessage = "There are no audit findings that can be converted automatically.";
            await _dialogs.ShowInformationAsync("Nothing to prepare", StatusMessage);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Prepare all supported findings",
            $"Review {candidates.Length} finding(s) and convert every supported title, slug, description, and missing-H1 issue into a concrete WordPress action? No WordPress change will be executed yet.");
        if (!confirmed) return;

        IsBusy = true;
        QueueState = "Preparing supported findings";
        CurrentStep = "Building concrete values for the current site";
        ProgressPercent = 15;
        using var operation = _operations.Begin(
            "Preparing supported findings",
            CurrentStep,
            $"Reviewing {candidates.Length} finding(s) and producing exact values where a safe adapter exists.",
            15);
        try
        {
            var result = await _service.PrepareExecutableValuesAsync(site.Id, candidates);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            ProgressPercent = 100;
            var value = result.Value;
            StatusMessage = $"Preparation finished: {value.Prepared} converted, {value.AlreadyExecutable} already concrete, {value.Unsupported} still require AI or a manual adapter.";
            await _dialogs.ShowInformationAsync("Preparation summary", StatusMessage);
        }
        finally
        {
            IsBusy = false;
            await LoadAsync();
        }
    }

    private async Task CompleteAndExecuteSelectedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var originalIds = SelectedCandidates().Select(x => x.ChangeId).Distinct().ToArray();
        if (originalIds.Length == 0)
        {
            StatusMessage = "Select one or more rows first.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Complete and execute selected",
            $"Prepare exact executable values for {originalIds.Length} selected row(s), ask for approval where required, then execute only the supported low/medium-risk actions with backup and verification?");
        if (!confirmed) return;

        IsBusy = true;
        QueueState = "Completing selected";
        CurrentStep = "Preparing executable values";
        ProgressPercent = 10;
        try
        {
            var preparation = await _service.PrepareExecutableValuesAsync(site.Id, originalIds);
            if (preparation.IsFailure)
            {
                StatusMessage = preparation.Error.Message;
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }

        await LoadAsync();
        var preparedRows = Items.Where(x => originalIds.Contains(x.ChangeId)).ToArray();
        var pending = preparedRows.Where(x => x.CanApprove && x.RiskLevel != "High" && !x.RequiresStaging).ToArray();
        foreach (var row in pending)
            await _changes.SetApprovalStatusAsync(row.ChangeId, "Approved");

        await LoadAsync();
        var executable = Items
            .Where(x => originalIds.Contains(x.ChangeId) && x.CanExecute && x.ExecutionStatus != "Executed")
            .Select(x => x.ChangeId)
            .ToArray();

        if (executable.Length == 0)
        {
            var needsValue = preparedRows.Count(NeedsExecutableValue);
            var highRisk = preparedRows.Count(x => x.RiskLevel == "High");
            var staging = preparedRows.Count(x => x.RequiresStaging);
            var unsupported = preparedRows.Length - needsValue - highRisk - staging;
            StatusMessage = $"No selected action is executable yet. Needs value/AI: {needsValue}; high risk: {highRisk}; staging: {staging}; unsupported/manual: {Math.Max(0, unsupported)}.";
            await _dialogs.ShowInformationAsync("Execution readiness summary", StatusMessage);
            return;
        }

        await RunBatchAsync(site.Id, executable, rollback: false, "CompleteAndExecuteSelected");
    }

    private void GoToFirstExecutable()
    {
        var row = Items.FirstOrDefault(x => x.CanExecute && x.ExecutionStatus != "Executed");
        if (row is null)
        {
            StatusMessage = "No executable row is available. Prepare a supported SEO title, slug, or description result first.";
            return;
        }

        SelectedItems.Clear();
        SelectedItems.Add(row);
        SelectedItem = row;
        StatusMessage = $"Selected the first executable action: {row.ObjectLabel} — {row.ChangeType}.";
        OnPropertyChanged(nameof(SelectedCount));
    }

    private static bool NeedsExecutableValue(ApprovedChangeExecutionItem item)
        => item.ExecutionStatus != "Executed" &&
           (!item.CanExecute &&
            (item.ChangeType.StartsWith("SEO_", StringComparison.OrdinalIgnoreCase) ||
             item.ChangeType is "TITLE_TOO_LONG" or "TITLE_TOO_SHORT" or "MISSING_SLUG" or "MISSING_EXCERPT"));

    private void BuildPlan()
    {
        PlanSafeCount = Items.Count(x =>
            (x.CanExecute || (x.CanApprove && x.RiskLevel == "Low")) &&
            !x.RequiresStaging && x.ExecutionStatus != "Executed");
        PlanReviewCount = Items.Count(x =>
            x.ExecutionStatus != "Executed" &&
            (x.RiskLevel == "Medium" || (x.ApprovalStatus == "Pending" && x.RiskLevel != "Low")));
        PlanManualCount = Items.Count(x =>
            x.ExecutionStatus != "Executed" &&
            ((!x.CanExecute && !x.CanApprove) || x.RequiresStaging || x.RiskLevel == "High"));
        PlanSummary = $"Phase 48 plan: {PlanSafeCount} safe action(s), {PlanReviewCount} needing review, and {PlanManualCount} manual/staging action(s).";
        StatusMessage = PlanSummary;
    }

    private async Task RunSafePlanAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var pendingSafe = Items.Where(x => x.CanApprove && x.RiskLevel == "Low" && !x.RequiresStaging).ToArray();
        var alreadyReady = Items.Where(x => x.CanExecute && x.ExecutionStatus != "Executed").ToArray();
        if (pendingSafe.Length == 0 && alreadyReady.Length == 0)
        {
            StatusMessage = "The safe plan contains no directly executable changes.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Run safe execution plan",
            $"Approve {pendingSafe.Length} pending low-risk change(s), then execute all directly supported ready changes?\n\nEvery execution will create a backup and verify the saved WordPress value.");
        if (!confirmed) return;

        if (pendingSafe.Length > 0)
        {
            IsBusy = true;
            QueueState = "Approving plan";
            try
            {
                for (var index = 0; index < pendingSafe.Length; index++)
                {
                    CurrentStep = $"Approving {index + 1} of {pendingSafe.Length}";
                    ProgressPercent = ((index + 1) * 30) / pendingSafe.Length;
                    await _changes.SetApprovalStatusAsync(pendingSafe[index].ChangeId, "Approved");
                }
            }
            finally { IsBusy = false; }
            await LoadAsync();
        }

        var readyIds = Items.Where(x => x.CanExecute && x.ExecutionStatus != "Executed").Select(x => x.ChangeId).ToArray();
        if (readyIds.Length == 0)
        {
            StatusMessage = "The plan was approved, but no item has a supported direct-execution adapter yet.";
            return;
        }

        await RunBatchAsync(site.Id, readyIds, rollback: false, "SafeExecutionPlan");
    }

    private void SelectReady()
    {
        SelectedItems.Clear();
        foreach (var item in Items.Where(x => x.CanExecute))
            SelectedItems.Add(item);
        SelectedItem = SelectedItems.FirstOrDefault();
        StatusMessage = $"Selected {SelectedItems.Count} ready change(s).";
        OnPropertyChanged(nameof(SelectedCount));
        NotifyCommands();
    }

    private void ClearSelection()
    {
        SelectedItems.Clear();
        SelectedItem = null;
        StatusMessage = "Selection cleared.";
        OnPropertyChanged(nameof(SelectedCount));
        NotifyCommands();
    }

    private async Task ExecuteSelectedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var candidates = SelectedCandidates().Where(x => x.ExecutionStatus != "Executed").ToArray();
        if (candidates.Length == 0)
        {
            StatusMessage = "Select one or more rows first.";
            return;
        }

        // Make the main action useful: pending low-risk supported rows are approved automatically,
        // then the queue is reloaded and the same rows are executed. High-risk, staging, and
        // unsupported rows remain blocked and are reported to the user.
        var pendingSafe = candidates
            .Where(x => x.CanApprove && x.RiskLevel == "Low" && !x.RequiresStaging)
            .ToArray();

        var candidateIds = candidates.Select(x => x.ChangeId).ToHashSet();
        if (pendingSafe.Length > 0)
        {
            var approveConfirmed = await _dialogs.ConfirmAsync(
                "Approve and execute selected changes",
                $"{pendingSafe.Length} selected low-risk change(s) are still pending. Approve them first, then execute every selected change that has a supported direct adapter?");
            if (!approveConfirmed) return;

            IsBusy = true;
            QueueState = "Approving selected";
            try
            {
                for (var index = 0; index < pendingSafe.Length; index++)
                {
                    CurrentStep = $"Approving {index + 1} of {pendingSafe.Length}";
                    ProgressPercent = ((index + 1) * 25) / pendingSafe.Length;
                    await _changes.SetApprovalStatusAsync(pendingSafe[index].ChangeId, "Approved");
                }
            }
            finally
            {
                IsBusy = false;
            }

            await LoadAsync();
        }

        var executableIds = Items
            .Where(x => candidateIds.Contains(x.ChangeId) && x.CanExecute && x.ExecutionStatus != "Executed")
            .Select(x => x.ChangeId)
            .ToArray();

        if (executableIds.Length == 0)
        {
            var blocked = candidates.Length;
            StatusMessage = $"None of the {blocked} selected row(s) can be executed directly. High-risk, staging-required, and unsupported change types must stay in manual review.";
            await _dialogs.ShowInformationAsync("Nothing executable", StatusMessage);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Execute selected changes",
            $"Execute {executableIds.Length} selected supported change(s) on WordPress?\n\nThe pipeline will validate, create a SQLite safety backup, update WordPress, read the object again, and verify the saved value.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, executableIds, rollback: false, "ApprovedChangeExecution");
    }

    private async Task ExecuteAllReadyAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;
        var ready = Items.Where(x => x.CanExecute && x.ExecutionStatus != "Executed").ToArray();
        if (ready.Length == 0)
        {
            StatusMessage = "There are no approved, supported, low/medium-risk changes ready to execute.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Execute all ready changes",
            $"Queue all {ready.Length} ready change(s) for sequential execution?\n\nBlocked, staging-required, and high-risk items will not be included.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, ready.Select(x => x.ChangeId).ToArray(), rollback: false, "ApprovedChangeExecutionAllReady");
    }

    private async Task RetryFailedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;
        var failed = Items.Where(x => x.CanExecute && x.ExecutionStatus == "Failed").ToArray();
        if (failed.Length == 0)
        {
            StatusMessage = "There are no failed executable changes to retry.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Retry failed changes",
            $"Retry {failed.Length} failed executable change(s)? Previous execution history will remain available in Jobs.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, failed.Select(x => x.ChangeId).ToArray(), rollback: false, "ApprovedChangeRetry");
    }

    private async Task RollbackSelectedAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;
        var selected = SelectedCandidates().Where(x => x.ExecutionStatus == "Executed").ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Select one or more successfully executed rows to roll back.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Rollback executed changes",
            $"Restore the previous value for {selected.Length} selected change(s)? A fresh local backup is created before rollback.");
        if (!confirmed) return;

        await RunBatchAsync(site.Id, selected.Select(x => x.ChangeId).ToArray(), rollback: true, "ApprovedChangeRollback");
    }

    private async Task RunBatchAsync(Guid siteId, Guid[] ids, bool rollback, string jobType)
    {
        IsBusy = true;
        ProgressPercent = 0;
        QueueState = rollback ? "Rolling back" : "Running";
        CurrentStep = "Creating execution job";
        var operationTitle = rollback ? "Rolling back WordPress changes" : "Executing safe WordPress changes";
        using var operation = _operations.Begin(
            operationTitle,
            CurrentStep,
            rollback
                ? $"Restoring {ids.Length} selected change(s) with backup and verification. The application is locked to protect the transaction."
                : $"Executing {ids.Length} approved change(s) with backup, WordPress verification, evidence, and recovery tracking.",
            0);
        _cts = new CancellationTokenSource();
        ResetLivePipeline(rollback);
        BeforeEvidencePath = null;
        AfterEvidencePath = null;
        EvidenceStatus = "Preparing execution evidence.";

        try
        {
            using (var gateScope = _scopeFactory.CreateScope())
            {
                var decision = await gateScope.ServiceProvider.GetRequiredService<IJobFailureGate>().CanStartAsync(siteId, jobType, _cts.Token);
                if (!decision.CanRun)
                {
                    QueueState = "Paused after failures";
                    StatusMessage = decision.Message;
                    CurrentStep = decision.ResumeAtUtc.HasValue ? $"Paused until {decision.ResumeAtUtc.Value.ToLocalTime():g}" : "Paused by job reliability settings";
                    return;
                }
            }
            CurrentJobId = await StartJobAsync(siteId, jobType, _cts.Token);
            _jobRegistration = _cancellationRegistry.Register(CurrentJobId.Value, _cts);
            SetPipelineStep("Create execution job", "Completed", $"Job {CurrentJobId}");

            var automation = await _settings.GetAiAutomationSettingsAsync(_cts.Token);
            var activeSite = _sites.SelectedSite;
            if (automation.CaptureBeforeAfterEvidence && activeSite is not null)
            {
                SetPipelineStep("Capture before evidence", "Running", "Opening the live site in responsive viewports");
                BeforeEvidencePath = await CaptureExecutionEvidenceAsync(activeSite.SiteUrl, "before", _cts.Token);
                SetPipelineStep("Capture before evidence",
                    string.IsNullOrWhiteSpace(BeforeEvidencePath) ? "Warning" : "Completed",
                    string.IsNullOrWhiteSpace(BeforeEvidencePath) ? EvidenceStatus : BeforeEvidencePath);
            }
            else
            {
                SetPipelineStep("Capture before evidence", "Skipped", "Disabled in AI Automation settings");
            }

            var progress = new Progress<(int Percent, string Step)>(p =>
            {
                ProgressPercent = p.Percent;
                CurrentStep = p.Step;
                _operations.Report(p.Percent, p.Step, rollback ? "Restoring and verifying the previous WordPress value." : "Applying the approved WordPress transaction and verifying the saved result.");
                UpdatePipelineFromProgress(p.Step);
                _ = ReportJobSafeAsync(CurrentJobId, p.Percent, p.Step);
            });

            var result = await ExecuteWithTransientRecoveryAsync(
                siteId,
                ids,
                rollback,
                progress,
                _cts.Token);

            LastExecutionUtc = DateTime.UtcNow;
            if (result.IsFailure)
            {
                QueueState = "Failed";
                StatusMessage = result.Error.Message;
                _operations.Fail(result.Error.Message);
                SetPipelineStep("Verify saved WordPress value", "Failed", result.Error.Message);
                await FailJobSafeAsync(CurrentJobId, result.Error.Message);
            }
            else
            {
                var value = result.Value;
                QueueState = value.Failed > 0 ? "Completed with failures" : "Completed";
                StatusMessage = $"Requested {value.Requested}; succeeded {value.Executed}; verified {value.Verified}; failed {value.Failed}; skipped {value.Skipped}.";
                if (value.Failed > 0) _operations.Fail(StatusMessage); else _operations.Complete(StatusMessage);
                SetPipelineStep("Send WordPress updates", value.Executed > 0 ? "Completed" : "Warning",
                    $"{value.Executed} update(s) accepted; {value.Failed} failed; {value.Skipped} skipped");
                SetPipelineStep("Verify saved WordPress value", value.Verified > 0 && value.Failed == 0 ? "Completed" : "Warning",
                    $"{value.Verified} verified result(s)");

                if (automation.CaptureBeforeAfterEvidence && activeSite is not null && value.Verified > 0)
                {
                    SetPipelineStep("Capture after evidence", "Running", "Reloading the live site after verified writes");
                    AfterEvidencePath = await CaptureExecutionEvidenceAsync(activeSite.SiteUrl, "after", _cts.Token);
                    SetPipelineStep("Capture after evidence",
                        string.IsNullOrWhiteSpace(AfterEvidencePath) ? "Warning" : "Completed",
                        string.IsNullOrWhiteSpace(AfterEvidencePath) ? EvidenceStatus : AfterEvidencePath);
                }
                else
                {
                    SetPipelineStep("Capture after evidence", "Skipped",
                        value.Verified == 0 ? "No verified write was available to capture" : "Disabled in AI Automation settings");
                }

                SetPipelineStep("Finalize log and recovery state", value.Failed > 0 ? "Warning" : "Completed",
                    value.Failed > 0 ? StatusMessage : "Job history, API responses, verification, and rollback state were saved.");

                if (value.Failed > 0)
                    await FailJobSafeAsync(CurrentJobId, StatusMessage);
                else
                    await CompleteJobSafeAsync(CurrentJobId);
            }
        }
        catch (OperationCanceledException)
        {
            MarkRunningPipelineSteps("Cancelled", "Execution cancelled by the user");
            QueueState = "Cancelled";
            StatusMessage = "The execution was cancelled. Completed items remain recorded and verified.";
            LastExecutionUtc = DateTime.UtcNow;
            await CancelJobSafeAsync(CurrentJobId);
        }
        catch (Exception exception)
        {
            MarkRunningPipelineSteps("Failed", exception.Message);
            QueueState = "Failed";
            StatusMessage = exception.Message;
            LastExecutionUtc = DateTime.UtcNow;
            await FailJobSafeAsync(CurrentJobId, exception.ToString());
            throw;
        }
        finally
        {
            _jobRegistration?.Dispose();
            _jobRegistration = null;
            _cts?.Dispose();
            _cts = null;
            CurrentJobId = null;
            IsBusy = false;
            await LoadAsync();
        }
    }


    private void BuildPreviewPipeline(ApprovedChangeExecutionItem? item)
    {
        PipelineSteps.Clear();
        if (item is null)
        {
            AddPipelineStep("Select an action", "Waiting", "Choose a queue row to see its exact execution route.");
            AddPipelineStep("Create safety backup", "Waiting", "Runs before the first WordPress write.");
            AddPipelineStep("Capture before evidence", "Waiting", "Controlled by AI Automation settings.");
            AddPipelineStep("Send WordPress updates", "Waiting", "Only supported adapters can write.");
            AddPipelineStep("Verify saved WordPress value", "Waiting", "Reads the live object again after the write.");
            AddPipelineStep("Capture after evidence", "Waiting", "Runs only after a verified result.");
            return;
        }

        AddPipelineStep("AI route selected", "Ready", $"{item.ExecutorName} • {item.RouteState}");
        AddPipelineStep("Create safety backup", item.RequiresBackup ? "Ready" : "Optional",
            item.RequiresBackup ? "A local SQLite recovery point will be created." : "This route does not require a database backup.");
        AddPipelineStep("Capture before evidence", "Ready", "Responsive screenshots are captured when evidence is enabled.");
        AddPipelineStep("Send WordPress updates", item.CanExecute ? "Ready" : "Blocked",
            item.CanExecute ? item.ExecutionPlan : item.PreflightMessage);
        AddPipelineStep("Verify saved WordPress value", item.CanExecute ? "Ready" : "Blocked",
            item.CanExecute ? "The same WordPress field is read again and compared with the requested value." : "Verification starts only after a supported write.");
        AddPipelineStep("Capture after evidence", item.CanExecute ? "Ready" : "Blocked",
            item.CanExecute ? "A second screenshot set is captured after verification." : "No after evidence is captured until the route becomes executable.");
    }

    private void ResetLivePipeline(bool rollback)
    {
        PipelineSteps.Clear();
        AddPipelineStep("Create execution job", "Running", rollback ? "Preparing a rollback job." : "Preparing an execution job.");
        AddPipelineStep("Create safety backup", "Waiting", "Protecting the local workspace before any write.");
        AddPipelineStep("Capture before evidence", "Waiting", "Waiting for AI Automation evidence policy.");
        AddPipelineStep("Read current WordPress value", "Waiting", "Loading the live object through the WordPress REST API.");
        AddPipelineStep(rollback ? "Send rollback update" : "Send WordPress updates", "Waiting",
            rollback ? "Restoring the previously recorded value." : "Writing only supported concrete fields.");
        AddPipelineStep("Verify saved WordPress value", "Waiting", "Reading the live object again and comparing the target field.");
        AddPipelineStep("Capture after evidence", "Waiting", "Capturing the verified visual result.");
        AddPipelineStep("Finalize log and recovery state", "Waiting", "Updating job history, API logs, and rollback availability.");
    }

    private void UpdatePipelineFromProgress(string step)
    {
        if (step.Contains("backup", StringComparison.OrdinalIgnoreCase))
        {
            SetPipelineStep("Create safety backup", "Completed", step);
            return;
        }

        if (step.Contains("Executing", StringComparison.OrdinalIgnoreCase) ||
            step.Contains("Rolling back", StringComparison.OrdinalIgnoreCase))
        {
            SetPipelineStep("Read current WordPress value", "Completed", "Live WordPress value loaded.");
            var writeStep = PipelineSteps.FirstOrDefault(x =>
                x.Name.Equals("Send WordPress updates", StringComparison.OrdinalIgnoreCase) ||
                x.Name.Equals("Send rollback update", StringComparison.OrdinalIgnoreCase));
            if (writeStep is not null)
            {
                writeStep.Status = "Running";
                writeStep.Detail = step;
            }
            return;
        }

        if (step.Contains("completed", StringComparison.OrdinalIgnoreCase))
        {
            SetPipelineStep("Finalize log and recovery state", "Running", step);
        }
    }

    private void AddPipelineStep(string name, string status, string detail)
        => PipelineSteps.Add(new ExecutionPipelineStep(PipelineSteps.Count + 1, name, status, detail));

    private void SetPipelineStep(string name, string status, string detail)
    {
        var row = PipelineSteps.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            AddPipelineStep(name, status, detail);
            return;
        }

        row.Status = status;
        row.Detail = detail;
    }

    private void MarkRunningPipelineSteps(string status, string detail)
    {
        foreach (var step in PipelineSteps.Where(x => x.Status == "Running"))
        {
            step.Status = status;
            step.Detail = detail;
        }
    }

    private async Task<string?> CaptureExecutionEvidenceAsync(string siteUrl, string stage, CancellationToken cancellationToken)
    {
        try
        {
            EvidenceStatus = $"Capturing {stage} responsive evidence.";
            var progress = new Progress<VisualInspectionProgress>(p =>
            {
                CurrentStep = $"{stage} evidence: {p.Step}";
                EvidenceStatus = $"{p.Percent}% — {p.Detail}";
            });
            var results = await _visualInspection.InspectAsync(siteUrl, progress, cancellationToken);
            var desktop = results.FirstOrDefault(x => x.ViewportName.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                ?? results.FirstOrDefault();
            if (desktop is null)
            {
                EvidenceStatus = $"No {stage} screenshot was returned.";
                return null;
            }

            EvidenceStatus = $"{stage} evidence captured for {results.Count} responsive viewport(s).";
            return desktop.ScreenshotPath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            EvidenceStatus = $"{stage} evidence was unavailable: {exception.Message}";
            return null;
        }
    }

    private static void OpenEvidence(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void CancelCurrent()
    {
        if (CurrentJobId.HasValue && _cancellationRegistry.TryCancel(CurrentJobId.Value))
        {
            QueueState = "Cancellation requested";
            CurrentStep = "Waiting for the current WordPress operation to stop safely";
            return;
        }

        _cts?.Cancel();
    }


    private async Task<AIWordPressManager.Application.Common.Results.Result<ChangeExecutionBatchResult>> ExecuteWithTransientRecoveryAsync(
        Guid siteId,
        IReadOnlyCollection<Guid> changeIds,
        bool rollback,
        IProgress<(int Percent, string Step)> progress,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 2;
        AIWordPressManager.Application.Common.Results.Result<ChangeExecutionBatchResult>? lastResult = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                const int retryDelaySeconds = 2;
                QueueState = "Self-healing retry";
                CurrentStep = $"Transient WordPress failure detected. Retrying in {retryDelaySeconds} seconds.";
                SetPipelineStep(
                    "Send WordPress updates",
                    "Running",
                    $"Automatic recovery attempt {attempt} of {maximumAttempts}");
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), cancellationToken);
            }

            lastResult = rollback
                ? await _service.RollbackAsync(siteId, changeIds, progress, cancellationToken)
                : await _service.ExecuteAsync(siteId, changeIds, progress, cancellationToken);

            if (lastResult.IsSuccess || !IsTransientExecutionFailure(lastResult.Error.Message) || attempt == maximumAttempts)
                return lastResult;

            SetPipelineStep(
                "Send WordPress updates",
                "Warning",
                $"Transient failure: {lastResult.Error.Message}");
        }

        return lastResult!;
    }

    private static bool IsTransientExecutionFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        string[] transientMarkers =
        [
            "timeout",
            "timed out",
            "temporarily unavailable",
            "connection reset",
            "connection refused",
            "network",
            "429",
            "too many requests",
            "502",
            "503",
            "504",
            "bad gateway",
            "service unavailable",
            "gateway timeout"
        ];

        return transientMarkers.Any(marker =>
            message.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Guid> StartJobAsync(Guid siteId, string jobType, CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IExecutionJobStore>().StartAsync(siteId, jobType, token);
    }

    private async Task ReportJobSafeAsync(Guid? jobId, int percent, string step)
    {
        if (!jobId.HasValue) return;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IExecutionJobStore>().ReportAsync(jobId.Value, percent, step);
        }
        catch
        {
            // UI progress must continue even if a non-critical history update fails.
        }
    }

    private async Task CompleteJobSafeAsync(Guid? jobId)
    {
        if (!jobId.HasValue) return;
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IExecutionJobStore>().CompleteAsync(jobId.Value);
    }

    private async Task FailJobSafeAsync(Guid? jobId, string error)
    {
        if (!jobId.HasValue) return;
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IExecutionJobStore>().FailAsync(jobId.Value, error);
    }

    private async Task CancelJobSafeAsync(Guid? jobId)
    {
        if (!jobId.HasValue) return;
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IExecutionJobStore>().CancelAsync(jobId.Value);
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(PendingApprovalCount));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(ExecutedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(BlockedCount));
        OnPropertyChanged(nameof(NeedsCompletionCount));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        ApproveSelectedCommand.NotifyCanExecuteChanged();
        ApproveAllLowRiskCommand.NotifyCanExecuteChanged();
        PrepareSelectedCommand.NotifyCanExecuteChanged();
        PrepareAllSupportedCommand.NotifyCanExecuteChanged();
        CompleteAndExecuteSelectedCommand.NotifyCanExecuteChanged();
        GoToFirstExecutableCommand.NotifyCanExecuteChanged();
        BuildPlanCommand.NotifyCanExecuteChanged();
        RunSafePlanCommand.NotifyCanExecuteChanged();
        SelectReadyCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ExecuteSelectedCommand.NotifyCanExecuteChanged();
        ExecuteAllReadyCommand.NotifyCanExecuteChanged();
        RetryFailedCommand.NotifyCanExecuteChanged();
        RollbackSelectedCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

}

public sealed partial class ExecutionPipelineStep : ObservableObject
{
    public ExecutionPipelineStep(int order, string name, string status, string detail)
    {
        Order = order;
        Name = name;
        _status = status;
        _detail = detail;
    }

    public int Order { get; }
    public string Name { get; }

    [ObservableProperty] private string _status;
    [ObservableProperty] private string _detail;
}
