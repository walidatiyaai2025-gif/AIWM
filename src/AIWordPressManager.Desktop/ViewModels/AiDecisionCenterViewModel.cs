using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class AiDecisionCenterViewModel : ObservableObject
{
    private readonly IApprovedChangeExecutionService _execution;
    private readonly SitesViewModel _sites;
    private readonly IDialogService _dialogs;
    private readonly IApplicationPathService _paths;

    public ObservableCollection<AiDecisionItem> Items { get; } = [];
    public ObservableCollection<string> Filters { get; } = ["All decisions", "Execute", "Approval", "Staging", "Blocked", "Needs value"];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ExecuteSelectedCommand { get; }
    public IAsyncRelayCommand PrepareSelectedCommand { get; }
    public IRelayCommand OpenExecutionCenterCommand { get; }
    public IRelayCommand OpenEvidenceCommand { get; }
    public IRelayCommand ClearFilterCommand { get; }

    public event Action<string>? NavigationRequested;

    [ObservableProperty] private AiDecisionItem? _selectedItem;
    [ObservableProperty] private string _selectedFilter = "All decisions";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Load the active site's queue to calculate AI execution decisions.";
    [ObservableProperty] private DateTime? _lastEvaluatedUtc;

    public int TotalCount => Items.Count;
    public int ExecuteCount => Items.Count(x => x.Decision == "Execute");
    public int ApprovalCount => Items.Count(x => x.Decision == "Approval");
    public int ProtectedCount => Items.Count(x => x.Decision is "Staging" or "Blocked");
    public string LastEvaluatedText => LastEvaluatedUtc is null ? "Not evaluated" : LastEvaluatedUtc.Value.ToLocalTime().ToString("g");

    public AiDecisionCenterViewModel(
        IApprovedChangeExecutionService execution,
        SitesViewModel sites,
        IDialogService dialogs,
        IApplicationPathService paths)
    {
        _execution = execution;
        _sites = sites;
        _dialogs = dialogs;
        _paths = paths;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ExecuteSelectedCommand = new AsyncRelayCommand(ExecuteSelectedAsync, () => !IsBusy);
        PrepareSelectedCommand = new AsyncRelayCommand(PrepareSelectedAsync, () => !IsBusy);
        OpenExecutionCenterCommand = new RelayCommand(() => NavigationRequested?.Invoke("Execution Center"));
        OpenEvidenceCommand = new RelayCommand(() => NavigationRequested?.Invoke("Evidence Center"));
        ClearFilterCommand = new RelayCommand(() => { SearchText = string.Empty; SelectedFilter = "All decisions"; ApplyFilter(); });
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        if (_sites.SelectedSite is null)
        {
            Status = "Select a site first.";
            return;
        }

        IsBusy = true;
        try
        {
            var queue = await _execution.GetApprovedQueueAsync(_sites.SelectedSite.Id);
            var decisions = queue.Select(BuildDecision).OrderByDescending(x => x.Priority).ThenByDescending(x => x.Confidence).ToList();
            _allItems = decisions;
            ApplyFilter();
            LastEvaluatedUtc = DateTime.UtcNow;
            Status = $"Evaluated {decisions.Count:N0} changes. {decisions.Count(x => x.Decision == "Execute"):N0} can enter the verified transaction pipeline now.";
            await WriteDecisionSnapshotAsync(decisions);
            NotifySummary();
        }
        catch (Exception ex)
        {
            Status = $"Decision evaluation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadCommand.NotifyCanExecuteChanged();
        }
    }

    private List<AiDecisionItem> _allItems = [];

    private void ApplyFilter()
    {
        IEnumerable<AiDecisionItem> query = _allItems;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(x => x.ObjectLabel.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.ChangeType.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Decision.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Executor.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedFilter switch
        {
            "Execute" => query.Where(x => x.Decision == "Execute"),
            "Approval" => query.Where(x => x.Decision == "Approval"),
            "Staging" => query.Where(x => x.Decision == "Staging"),
            "Blocked" => query.Where(x => x.Decision == "Blocked"),
            "Needs value" => query.Where(x => x.Decision == "Needs value"),
            _ => query
        };

        Items.Clear();
        foreach (var item in query) Items.Add(item);
        NotifySummary();
    }

    private async Task PrepareSelectedAsync()
    {
        if (_sites.SelectedSite is null || SelectedItem is null)
        {
            await _dialogs.ShowInformationAsync("AI Decision Engine", "Select a decision first.");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _execution.PrepareExecutableValuesAsync(_sites.SelectedSite.Id, [SelectedItem.ChangeId]);
            Status = result.IsSuccess
                ? $"Prepared {result.Value?.Prepared ?? 0} executable value(s). Reloading decisions..."
                : string.IsNullOrWhiteSpace(result.Error.Message) ? "Preparation failed." : result.Error.Message;
            await LoadAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task ExecuteSelectedAsync()
    {
        if (_sites.SelectedSite is null || SelectedItem is null)
        {
            await _dialogs.ShowInformationAsync("AI Decision Engine", "Select a decision first.");
            return;
        }

        if (SelectedItem.Decision != "Execute")
        {
            await _dialogs.ShowInformationAsync("Protected decision", SelectedItem.PolicyExplanation);
            return;
        }

        var confirm = await _dialogs.ConfirmAsync(
            "Execute verified transaction",
            $"Execute {SelectedItem.ChangeType} on {_sites.SelectedSite.Name}?\n\nThe transaction will use backup, WordPress response logging, verification, evidence, and rollback support where available.");
        if (!confirm) return;

        IsBusy = true;
        var transactionId = Guid.NewGuid();
        await AppendTransactionAsync(transactionId, SelectedItem, "Started", "Decision approved by policy engine.");
        try
        {
            var progress = new Progress<(int Percent, string Step)>(x => Status = $"{x.Percent}% • {x.Step}");
            var result = await _execution.ExecuteAsync(_sites.SelectedSite.Id, [SelectedItem.ChangeId], progress);
            var state = result.IsSuccess && (result.Value?.Verified ?? 0) > 0 ? "Committed" : "Failed";
            var detail = result.IsSuccess
                ? $"Executed={result.Value?.Executed ?? 0}; Verified={result.Value?.Verified ?? 0}; Failed={result.Value?.Failed ?? 0}"
                : string.IsNullOrWhiteSpace(result.Error.Message) ? "Execution failed." : result.Error.Message;
            await AppendTransactionAsync(transactionId, SelectedItem, state, detail);
            Status = $"Transaction {state.ToLowerInvariant()}: {detail}";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await AppendTransactionAsync(transactionId, SelectedItem, "Failed", ex.ToString());
            Status = $"Transaction failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private static AiDecisionItem BuildDecision(ApprovedChangeExecutionItem item)
    {
        var hasValue = !string.IsNullOrWhiteSpace(item.ProposedValue);
        var highRisk = item.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase);
        var decision = item.RequiresStaging ? "Staging"
            : highRisk ? "Approval"
            : !hasValue ? "Needs value"
            : item.CanExecute && item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Execute"
            : item.CanApprove ? "Approval"
            : "Blocked";

        var confidence = decision switch
        {
            "Execute" => 96,
            "Approval" => highRisk ? 72 : 86,
            "Needs value" => 64,
            "Staging" => 58,
            _ => 45
        };

        var impact = item.ChangeType.Contains("Title", StringComparison.OrdinalIgnoreCase) ? "High SEO visibility"
            : item.ChangeType.Contains("Content", StringComparison.OrdinalIgnoreCase) ? "High content impact"
            : item.ChangeType.Contains("Excerpt", StringComparison.OrdinalIgnoreCase) ? "Medium CTR impact"
            : item.ChangeType.Contains("Slug", StringComparison.OrdinalIgnoreCase) ? "High URL risk"
            : "Targeted improvement";

        var policy = decision switch
        {
            "Execute" => "Low-risk, approved, concrete value, supported executor, and verification path are available.",
            "Approval" => "A human decision is required because the action is pending or materially affects visible content.",
            "Staging" => "The change must be tested in staging before production execution.",
            "Needs value" => "The suggestion is valid but does not yet contain a concrete value that an executor can write.",
            _ => "No supported safe adapter is available for direct production execution."
        };

        return new AiDecisionItem(
            item.ChangeId, item.ObjectLabel, item.ChangeType, item.RiskLevel, item.ExecutorName,
            decision, confidence, impact, policy, item.ExecutionPlan, item.BeforePreview,
            item.AfterPreview, item.RequiresBackup, item.RequiresStaging,
            decision == "Execute" ? 100 : decision == "Approval" ? 75 : 35);
    }

    private async Task WriteDecisionSnapshotAsync(IReadOnlyCollection<AiDecisionItem> decisions)
    {
        var folder = Path.Combine(_paths.GetApplicationDataDirectory(), "DecisionEngine");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"decisions-{_sites.SelectedSite!.Id}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(decisions, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task AppendTransactionAsync(Guid transactionId, AiDecisionItem item, string state, string details)
    {
        var folder = Path.Combine(_paths.GetApplicationDataDirectory(), "Transactions");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "wordpress-transactions.jsonl");
        var entry = new
        {
            utc = DateTime.UtcNow,
            transactionId,
            siteId = _sites.SelectedSite?.Id,
            site = _sites.SelectedSite?.Name,
            item.ChangeId,
            item.ChangeType,
            item.Executor,
            item.Decision,
            state,
            details
        };
        await File.AppendAllTextAsync(file, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ExecuteCount));
        OnPropertyChanged(nameof(ApprovalCount));
        OnPropertyChanged(nameof(ProtectedCount));
        OnPropertyChanged(nameof(LastEvaluatedText));
    }
}

public sealed record AiDecisionItem(
    Guid ChangeId,
    string ObjectLabel,
    string ChangeType,
    string Risk,
    string Executor,
    string Decision,
    int Confidence,
    string ExpectedImpact,
    string PolicyExplanation,
    string ExecutionPlan,
    string BeforePreview,
    string AfterPreview,
    bool RequiresBackup,
    bool RequiresStaging,
    int Priority);
