using System.Collections.ObjectModel;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SuggestedChangesViewModel
{
    public ObservableCollection<ApprovalJourneyRequirement> ApprovalJourneyRequirements { get; } = [];

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

    public int DecidedCount => ApprovedCount + RejectedCount;
    public int ExecutionReadyCount => Items.Count(IsExecutionReadyApproval);

    internal void RefreshApprovalJourneyReadiness()
    {
        var hasSite = _sites.SelectedSite is not null;
        var hasLoadedReview = Items.Count > 0;
        var hasDecision = DecidedCount > 0;
        var hasApproved = ApprovedCount > 0;
        var approvedArePlanned = Items
            .Where(item => item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            .All(IsExecutionReadyApproval);
        var hasExecutionReady = ExecutionReadyCount > 0;

        ReplaceApprovalRequirements([
            new ApprovalJourneyRequirement("Active site", "The approval queue belongs to the selected WordPress site.", hasSite),
            new ApprovalJourneyRequirement("Review queue loaded", "Pending, approved and rejected decisions are loaded from SQLite.", hasLoadedReview),
            new ApprovalJourneyRequirement("Decision recorded", "At least one proposal has been approved or rejected.", hasDecision),
            new ApprovalJourneyRequirement("Approved change", "At least one proposal is explicitly approved for execution.", hasApproved),
            new ApprovalJourneyRequirement("Execution plan verified", "Approved proposals include risk and execution routing metadata.", approvedArePlanned && hasExecutionReady)
        ]);

        IsApprovalJourneyReady = hasSite && hasLoadedReview && hasDecision && hasApproved && approvedArePlanned && hasExecutionReady;
        ApprovalJourneyStatus = IsApprovalJourneyReady
            ? $"Approval Queue is complete. {ExecutionReadyCount} approved change(s) can enter Execution Center."
            : BuildApprovalJourneyStatus();

        OnPropertyChanged(nameof(DecidedCount));
        OnPropertyChanged(nameof(ExecutionReadyCount));
    }

    private static bool IsExecutionReadyApproval(SuggestedChangeItem item)
    {
        if (!item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return false;

        var hasRisk = !string.IsNullOrWhiteSpace(item.RiskLevel);
        var hasChangeType = !string.IsNullOrWhiteSpace(item.ChangeType);
        var hasTarget = !string.IsNullOrWhiteSpace(item.ObjectType) && item.ObjectId > 0;
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
