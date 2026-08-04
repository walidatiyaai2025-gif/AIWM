using AIWordPressManager.Application.Changes;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SuggestedChangesViewModel
{
    public int EstimateAiConfidence(SuggestedChangeItem item)
    {
        var confidence = 62;

        if (item.CanApplyDirectly) confidence += 18;
        if (!item.RequiresStaging) confidence += 8;
        if (item.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase)) confidence += 10;
        else if (item.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase)) confidence -= 18;

        if (!string.IsNullOrWhiteSpace(item.ProposedValue)) confidence += 5;
        if (!string.IsNullOrWhiteSpace(item.CleanReason)) confidence += 3;

        return Math.Clamp(confidence, 35, 98);
    }

    public int EstimateAiPriorityScore(SuggestedChangeItem item)
    {
        var score = EstimateAiConfidence(item);

        if (item.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase)) score += 35;
        if (item.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase)) score += 25;
        if (item.CanApplyDirectly) score += 20;
        if (item.RequiresStaging) score -= 18;
        if (item.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase)) score -= 30;

        var type = item.ChangeType ?? string.Empty;
        if (type.Contains("Title", StringComparison.OrdinalIgnoreCase)) score += 18;
        if (type.Contains("Description", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (type.Contains("Alt", StringComparison.OrdinalIgnoreCase)) score += 12;
        if (type.Contains("Link", StringComparison.OrdinalIgnoreCase)) score += 10;

        return score;
    }

    public string GetAiConfidenceLabel(SuggestedChangeItem item)
    {
        var confidence = EstimateAiConfidence(item);
        return confidence >= 85 ? "High" : confidence >= 68 ? "Medium" : "Low";
    }

    public string GetAiPriorityLabel(SuggestedChangeItem item)
    {
        var score = EstimateAiPriorityScore(item);
        return score >= 125 ? "Critical" : score >= 100 ? "High" : score >= 75 ? "Medium" : "Low";
    }

    public SuggestedChangeItem? GetTopAiInboxItem() => Items
        .Where(x => x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(EstimateAiPriorityScore)
        .ThenByDescending(EstimateAiConfidence)
        .FirstOrDefault();

    public void ApplySmartQueue()
    {
        var ordered = Items
            .OrderByDescending(x => x.ApprovalStatus.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(EstimateAiPriorityScore)
            .ThenByDescending(EstimateAiConfidence)
            .ToArray();

        Items.Clear();
        foreach (var item in ordered) Items.Add(item);

        SelectedItem = GetTopAiInboxItem();
        StatusMessage = SelectedItem is null
            ? "Smart queue is ready. No pending AI proposals require review."
            : $"Smart queue prioritized {Items.Count} proposal(s). Top action: {SelectedItem.ChangeType} with {EstimateAiConfidence(SelectedItem)}% confidence.";

        RaiseCounts();
    }
}
