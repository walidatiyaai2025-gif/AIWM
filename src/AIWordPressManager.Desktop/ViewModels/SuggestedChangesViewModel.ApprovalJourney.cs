using System.Collections.ObjectModel;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SuggestedChangesViewModel
{
    public ObservableCollection<ApprovalJourneyRequirement> ApprovalJourneyRequirements { get; } = [];

    private readonly SemaphoreSlim _approvalJourneyRefreshLock = new(1, 1);
    private IReadOnlyList<SuggestedChangeItem> _approvalJourneySnapshot = [];
    private bool _isApprovalJourneyReady;
    private string _approvalJourneyStatus = "Review pending proposals and approve at least one safe, fully described change.";

    public bool IsApprovalJourneyReady
    {
        get => _isApprovalJourneyReady;
        private set => SetProperty(ref _isApprovalJourneyReady, value);
    }

    public string ApprovalJourneyStatus
    {
        get => _approvalJourneyStatus;
        private set => SetProperty(ref _approvalJourneyStatus, value);
    }

    public int ApprovalPendingCount => _approvalJourneySnapshot.Count(x => x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int ApprovalApprovedCount => _approvalJourneySnapshot.Count(x => x.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase));
    public int ApprovalRejectedCount => _approvalJourneySnapshot.Count(x => x.ApprovalStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase));
    public int DecidedCount => ApprovalApprovedCount + ApprovalRejectedCount;
    public int ExecutionReadyCount => _approvalJourneySnapshot.Count(IsExecutionReadyApproval);

    internal async Task RefreshApprovalJourneyReadinessAsync()
    {
        if (!await _approvalJourneyRefreshLock.WaitAsync(0))
            return;

        try
        {
            if (_sites.SelectedSite is null)
            {
                _approvalJourneySnapshot = [];
                RefreshApprovalJourneyReadiness();
                return;
            }

            var lease = _siteOperationGuard.Begin("Loading approval journey state");
            var rows = await _service.GetAsync(lease.SiteId, null);
            _siteOperationGuard.EnsureCurrent(lease);
            _approvalJourneySnapshot = rows;
            RefreshApprovalJourneyReadiness();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _approvalJourneyRefreshLock.Release();
        }
    }

    internal void RefreshApprovalJourneyReadiness()
    {
        var hasSite = _sites.SelectedSite is not null;
        var hasLoadedReview = _approvalJourneySnapshot.Count > 0;
        var hasDecision = DecidedCount > 0;
        var hasApproved = ApprovalApprovedCount > 0;
        var approved = _approvalJourneySnapshot
            .Where(item => item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var approvedArePlanned = approved.Length > 0 && approved.All(IsExecutionReadyApproval);
        var hasExecutionReady = ExecutionReadyCount > 0;

        ReplaceApprovalRequirements([
            new ApprovalJourneyRequirement("Active site", "The approval queue belongs to the selected WordPress site.", hasSite),
            new ApprovalJourneyRequirement("Review queue loaded", "Pending, approved and rejected decisions are loaded from SQLite.", hasLoadedReview),
            new ApprovalJourneyRequirement("Decision recorded", "At least one proposal has been approved or rejected.", hasDecision),
            new ApprovalJourneyRequirement("Approved change", "At least one proposal is explicitly approved for execution.", hasApproved),
            new ApprovalJourneyRequirement("Execution plan verified", "Approved proposals include target, risk and execution routing metadata.", approvedArePlanned && hasExecutionReady)
        ]);

        IsApprovalJourneyReady = hasSite && hasLoadedReview && hasDecision && hasApproved && approvedArePlanned && hasExecutionReady;
        ApprovalJourneyStatus = IsApprovalJourneyReady
            ? $"Approval Queue is complete. {ExecutionReadyCount} approved change(s) can enter Execution Center."
            : BuildApprovalJourneyStatus();

        OnPropertyChanged(nameof(ApprovalPendingCount));
        OnPropertyChanged(nameof(ApprovalApprovedCount));
        OnPropertyChanged(nameof(ApprovalRejectedCount));
        OnPropertyChanged(nameof(DecidedCount));
        OnPropertyChanged(nameof(ExecutionReadyCount));
    }

    private static bool IsExecutionReadyApproval(SuggestedChangeItem item)
    {
        if (!item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return false;

        var hasRisk = !string.IsNullOrWhiteSpace(item.RiskLevel);
        var hasChangeType = !string.IsNullOrWhiteSpace(item.ChangeType);
        var hasTarget = !string.IsNullOrWhiteSpace(item.ObjectType) && !string.IsNullOrWhiteSpace(item.ObjectId);
        var hasProposal = !string.IsNullOrWhiteSpace(item.ProposedValue);
        var hasRoute = item.CanApplyDirectly || item.RequiresStaging || !string.IsNullOrWhiteSpace(item.CleanReason);
        return hasRisk && hasChangeType && hasTarget && hasProposal && hasRoute;
    }

    private string BuildApprovalJourneyStatus()
    {
        var next = ApprovalJourneyRequirements.FirstOrDefault(item => !item.IsCompleted);
        return next is null
            ? "Approval Queue is ready."
            : $"Next requirement: {next.Title} — {next.Description}";
    }

    private void ReplaceApprovalRequirements(IEnumerable<ApprovalJourneyRequirement> values)
    {
        ApprovalJourneyRequirements.Clear();
        foreach (var value in values)
            ApprovalJourneyRequirements.Add(value);
    }
}

public sealed record ApprovalJourneyRequirement(string Title, string Description, bool IsCompleted)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
}
