using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SuggestedChangesViewModel
{
    public ObservableCollection<SuggestedChangesJourneyRequirement> FirstJourneyRequirements { get; } = [];

    [ObservableProperty] private bool _isFirstJourneyReady;
    [ObservableProperty] private string _firstJourneyStatus = "Generate proposals from the completed SEO baseline.";

    public int FirstJourneyReviewedCount => Items.Count(item =>
        !string.IsNullOrWhiteSpace(item.CurrentValue) &&
        !string.IsNullOrWhiteSpace(item.ProposedValue));

    public int FirstJourneyRiskClassifiedCount => Items.Count(item =>
        !string.IsNullOrWhiteSpace(item.RiskLevel));

    public int FirstJourneyRoutedCount => Items.Count(item =>
        item.CanApplyDirectly || item.RequiresStaging ||
        !string.IsNullOrWhiteSpace(item.ChangeType));

    internal void RefreshFirstJourneyReadiness()
    {
        var hasProposals = Items.Count > 0;
        var hasBeforeAfter = hasProposals && FirstJourneyReviewedCount == Items.Count;
        var hasRiskClassification = hasProposals && FirstJourneyRiskClassifiedCount == Items.Count;
        var hasExecutionRoute = hasProposals && FirstJourneyRoutedCount == Items.Count;
        var hasPendingApproval = PendingCount > 0;

        ReplaceRequirements(
            new SuggestedChangesJourneyRequirement("Generated proposals", hasProposals, hasProposals ? $"{Items.Count} proposal(s) loaded" : "Generate proposals from the SEO baseline"),
            new SuggestedChangesJourneyRequirement("Before / after review", hasBeforeAfter, hasBeforeAfter ? "Current and proposed values are available" : "Every proposal needs current and proposed values"),
            new SuggestedChangesJourneyRequirement("Risk classification", hasRiskClassification, hasRiskClassification ? "Risk levels are assigned" : "Classify every proposal by risk"),
            new SuggestedChangesJourneyRequirement("Execution routing", hasExecutionRoute, hasExecutionRoute ? "Direct, staging, or specialist route assigned" : "Define how each proposal will be executed"),
            new SuggestedChangesJourneyRequirement("Approval candidates", hasPendingApproval, hasPendingApproval ? $"{PendingCount} item(s) await approval" : "Keep at least one reviewed proposal pending"));

        IsFirstJourneyReady = hasProposals && hasBeforeAfter && hasRiskClassification && hasExecutionRoute && hasPendingApproval;
        FirstJourneyStatus = IsFirstJourneyReady
            ? $"Proposal review is complete. {PendingCount} item(s) are ready for Approval Queue."
            : FirstJourneyRequirements.First(requirement => !requirement.IsCompleted).Detail;

        OnPropertyChanged(nameof(FirstJourneyReviewedCount));
        OnPropertyChanged(nameof(FirstJourneyRiskClassifiedCount));
        OnPropertyChanged(nameof(FirstJourneyRoutedCount));
    }

    private void ReplaceRequirements(params SuggestedChangesJourneyRequirement[] requirements)
    {
        FirstJourneyRequirements.Clear();
        foreach (var requirement in requirements)
            FirstJourneyRequirements.Add(requirement);
    }
}

public sealed record SuggestedChangesJourneyRequirement(string Title, bool IsCompleted, string Detail)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
}
