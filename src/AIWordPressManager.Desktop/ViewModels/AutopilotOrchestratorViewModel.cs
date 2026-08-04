using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class AutopilotOrchestratorViewModel : ObservableObject
{
    private readonly Sites.SitesViewModel _sites;
    private readonly ContentAuditViewModel _contentAudit;
    private readonly SeoAuditViewModel _seoAudit;
    private readonly BrokenLinksViewModel _brokenLinks;
    private readonly SuggestedChangesViewModel _suggestedChanges;
    private readonly ExecutionCenterViewModel _executionCenter;
    private readonly JobsViewModel _jobs;
    private readonly IApplicationPathService _paths;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<AutopilotStageItem> Stages { get; } = [];
    public ObservableCollection<AutopilotEventItem> Timeline { get; } = [];
    public ObservableCollection<string> Modes { get; } = ["Monitor Only", "Suggest", "Semi-Automatic", "Fully Automatic"];

    [ObservableProperty] private string _selectedMode = "Semi-Automatic";
    [ObservableProperty] private bool _allowMetaChanges = true;
    [ObservableProperty] private bool _allowContentChanges = true;
    [ObservableProperty] private bool _allowVisualCss;
    [ObservableProperty] private bool _requireApprovalForTitles = true;
    [ObservableProperty] private bool _neverPublishAutomatically = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusMessage = "Ready to orchestrate the selected site.";
    [ObservableProperty] private string _currentStage = "Idle";
    [ObservableProperty] private string _activeSite = "No site selected";
    [ObservableProperty] private string _lastRun = "Never";
    [ObservableProperty] private int _detectedIssues;
    [ObservableProperty] private int _generatedActions;
    [ObservableProperty] private int _readyActions;
    [ObservableProperty] private int _failedJobs;

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SavePolicyCommand { get; }
    public IAsyncRelayCommand RunFullWorkflowCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand OpenExecutionCommand { get; }

    public event Action<string>? NavigationRequested;

    public AutopilotOrchestratorViewModel(
        Sites.SitesViewModel sites,
        ContentAuditViewModel contentAudit,
        SeoAuditViewModel seoAudit,
        BrokenLinksViewModel brokenLinks,
        SuggestedChangesViewModel suggestedChanges,
        ExecutionCenterViewModel executionCenter,
        JobsViewModel jobs,
        IApplicationPathService paths)
    {
        _sites = sites;
        _contentAudit = contentAudit;
        _seoAudit = seoAudit;
        _brokenLinks = brokenLinks;
        _suggestedChanges = suggestedChanges;
        _executionCenter = executionCenter;
        _jobs = jobs;
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SavePolicyCommand = new AsyncRelayCommand(SavePolicyAsync);
        RunFullWorkflowCommand = new AsyncRelayCommand(RunFullWorkflowAsync, () => !IsBusy && _sites.SelectedSite is not null);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenExecutionCommand = new AsyncRelayCommand(() => { NavigationRequested?.Invoke("Execution Center"); return Task.CompletedTask; });
        BuildStages();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RunFullWorkflowCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        ActiveSite = _sites.SelectedSite?.Name ?? "No site selected";
        await LoadPolicyAsync();
        RefreshSummary();
    }

    private async Task RunFullWorkflowAsync()
    {
        if (_sites.SelectedSite is null || IsBusy) return;
        _runCts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        Timeline.Clear();
        ResetStages();
        ActiveSite = _sites.SelectedSite.Name;
        StatusMessage = $"Autopilot is analyzing {ActiveSite}.";

        try
        {
            await RunStageAsync(0, "Load synchronized site data", async () =>
            {
                await _contentAudit.LoadAsync();
                await _seoAudit.LoadAsync();
                await _brokenLinks.LoadAsync();
            });

            await RunStageAsync(1, "Run measurable audits", async () =>
            {
                await _contentAudit.RunAuditCommand.ExecuteAsync(null);
                await _seoAudit.RunAuditCommand.ExecuteAsync(null);
            });

            await RunStageAsync(2, "Build AI action queue", async () =>
            {
                await _suggestedChanges.ShowAllAsync();
                await _executionCenter.LoadAsync();
                _executionCenter.BuildPlanCommand.Execute(null);
            });

            await RunStageAsync(3, "Apply approval policy", async () =>
            {
                if (SelectedMode is "Semi-Automatic" or "Fully Automatic")
                    await _executionCenter.ApproveAllLowRiskCommand.ExecuteAsync(null);
                else
                    await Task.Delay(100);
            });

            await RunStageAsync(4, "Prepare executable actions", async () =>
            {
                if (SelectedMode != "Monitor Only")
                    await _executionCenter.PrepareAllSupportedCommand.ExecuteAsync(null);
                else
                    await Task.Delay(100);
            });

            await RunStageAsync(5, "Execute and verify", async () =>
            {
                if (SelectedMode == "Fully Automatic")
                    await _executionCenter.RunSafePlanCommand.ExecuteAsync(null);
                else
                    await Task.Delay(100);
            });

            await RunStageAsync(6, "Refresh jobs and evidence", async () =>
            {
                await _jobs.LoadAsync();
                await _executionCenter.LoadAsync();
            });

            ProgressPercent = 100;
            CurrentStage = "Completed";
            LastRun = DateTime.Now.ToString("g");
            StatusMessage = SelectedMode == "Fully Automatic"
                ? "Workflow completed. Safe actions were executed and verified; protected actions remain queued."
                : "Workflow completed. Review the prepared actions in Approval Queue and Execution Center.";
            AddTimeline("Workflow", StatusMessage, "Completed");
        }
        catch (OperationCanceledException)
        {
            CurrentStage = "Cancelled";
            StatusMessage = "Autopilot workflow was cancelled safely.";
            AddTimeline("Workflow", StatusMessage, "Cancelled");
        }
        catch (Exception ex)
        {
            CurrentStage = "Failed";
            StatusMessage = $"Autopilot stopped: {ex.Message}";
            AddTimeline("Workflow", ex.Message, "Failed");
        }
        finally
        {
            IsBusy = false;
            _runCts?.Dispose();
            _runCts = null;
            RefreshSummary();
        }
    }

    private async Task RunStageAsync(int index, string name, Func<Task> action)
    {
        _runCts?.Token.ThrowIfCancellationRequested();
        var stage = Stages[index];
        stage.Status = "Running";
        stage.Detail = name;
        CurrentStage = name;
        ProgressPercent = (int)Math.Round(index * 100d / Stages.Count);
        AddTimeline(stage.Name, name, "Running");
        try
        {
            await action();
            _runCts?.Token.ThrowIfCancellationRequested();
            stage.Status = "Completed";
            stage.CompletedAt = DateTime.Now;
            ProgressPercent = (int)Math.Round((index + 1) * 100d / Stages.Count);
            AddTimeline(stage.Name, "Completed successfully", "Completed");
        }
        catch
        {
            stage.Status = "Failed";
            AddTimeline(stage.Name, "Stage failed; no later stage was executed.", "Failed");
            throw;
        }
    }

    private void Cancel() => _runCts?.Cancel();

    private void RefreshSummary()
    {
        DetectedIssues = _contentAudit.Issues.Count + _seoAudit.Issues.Count + _brokenLinks.BrokenCount;
        GeneratedActions = _suggestedChanges.Items.Count;
        ReadyActions = _executionCenter.ReadyCount;
        FailedJobs = _jobs.FailedCount;
    }

    private void BuildStages()
    {
        Stages.Clear();
        Stages.Add(new("Discover", "Load synchronized site data"));
        Stages.Add(new("Audit", "Run content, SEO, and link checks"));
        Stages.Add(new("Plan", "Convert findings into concrete AI actions"));
        Stages.Add(new("Policy", "Apply the site automation policy"));
        Stages.Add(new("Prepare", "Create backups and preflight executable actions"));
        Stages.Add(new("Execute", "Write safe changes and verify WordPress responses"));
        Stages.Add(new("Evidence", "Refresh jobs, logs, screenshots, and recovery state"));
    }

    private void ResetStages()
    {
        foreach (var stage in Stages)
        {
            stage.Status = "Waiting";
            stage.CompletedAt = null;
        }
    }

    private void AddTimeline(string title, string detail, string status) =>
        Timeline.Insert(0, new AutopilotEventItem(DateTime.Now, title, detail, status));

    private string PolicyPath
    {
        get
        {
            var directory = Path.Combine(_paths.GetApplicationDataDirectory(), "Orchestrator");
            Directory.CreateDirectory(directory);
            var id = _sites.SelectedSite?.Id.ToString() ?? "default";
            return Path.Combine(directory, $"policy-{id}.json");
        }
    }

    private async Task LoadPolicyAsync()
    {
        if (!File.Exists(PolicyPath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(PolicyPath);
            var p = JsonSerializer.Deserialize<AutopilotPolicy>(json);
            if (p is null) return;
            SelectedMode = p.Mode;
            AllowMetaChanges = p.AllowMetaChanges;
            AllowContentChanges = p.AllowContentChanges;
            AllowVisualCss = p.AllowVisualCss;
            RequireApprovalForTitles = p.RequireApprovalForTitles;
            NeverPublishAutomatically = p.NeverPublishAutomatically;
        }
        catch { }
    }

    private async Task SavePolicyAsync()
    {
        var policy = new AutopilotPolicy(SelectedMode, AllowMetaChanges, AllowContentChanges, AllowVisualCss, RequireApprovalForTitles, NeverPublishAutomatically);
        await File.WriteAllTextAsync(PolicyPath, JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true }));
        StatusMessage = $"Autopilot policy saved for {ActiveSite}.";
    }
}

public sealed partial class AutopilotStageItem(string name, string detail) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private string _detail = detail;
    [ObservableProperty] private string _status = "Waiting";
    [ObservableProperty] private DateTime? _completedAt;
}

public sealed record AutopilotEventItem(DateTime Time, string Title, string Detail, string Status);
public sealed record AutopilotPolicy(string Mode, bool AllowMetaChanges, bool AllowContentChanges, bool AllowVisualCss, bool RequireApprovalForTitles, bool NeverPublishAutomatically);
