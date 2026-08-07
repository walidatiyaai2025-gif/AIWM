using System.Collections.ObjectModel;
using System.IO;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class EvidenceCenterViewModel
{
    public ObservableCollection<EvidenceJourneyRequirement> FirstJourneyRequirements { get; } = [];

    private bool _isFirstJourneyReady;
    private string _firstJourneyStatus = "Load the execution receipt and verify the before/after evidence pair.";

    public bool IsFirstJourneyReady
    {
        get => _isFirstJourneyReady;
        private set => SetProperty(ref _isFirstJourneyReady, value);
    }

    public string FirstJourneyStatus
    {
        get => _firstJourneyStatus;
        private set => SetProperty(ref _firstJourneyStatus, value);
    }

    public int ReceiptCount => Items.Count(item => item.Category.Equals("Receipt", StringComparison.OrdinalIgnoreCase));
    public int OpenableEvidenceCount => Items.Count(item => File.Exists(item.FilePath));
    public bool HasSelectedEvidence => SelectedItem is not null && File.Exists(SelectedItem.FilePath);

    internal void RefreshFirstJourneyReadiness()
    {
        var hasEvidence = TotalCount > 0;
        var hasReceipt = ReceiptCount > 0;
        var hasBefore = BeforeCount > 0;
        var hasAfter = AfterCount > 0;
        var hasVerifiedPair = VerifiedPairCount > 0;
        var hasOpenableRecord = OpenableEvidenceCount > 0;

        ReplaceFirstJourneyRequirements([
            new EvidenceJourneyRequirement("Evidence loaded", "Evidence Center loaded local execution artifacts.", hasEvidence),
            new EvidenceJourneyRequirement("Execution receipt", "The HTML or JSON execution receipt is available.", hasReceipt),
            new EvidenceJourneyRequirement("Before evidence", "At least one before-state artifact is available.", hasBefore),
            new EvidenceJourneyRequirement("After evidence", "At least one after-state artifact is available.", hasAfter),
            new EvidenceJourneyRequirement("Verified pair", "A before/after pair can be reviewed together.", hasVerifiedPair),
            new EvidenceJourneyRequirement("Openable record", "At least one artifact still exists on disk and can be opened.", hasOpenableRecord)
        ]);

        IsFirstJourneyReady = hasEvidence && hasReceipt && hasBefore && hasAfter && hasVerifiedPair && hasOpenableRecord;
        FirstJourneyStatus = IsFirstJourneyReady
            ? $"First user journey completed. {TotalCount} artifact(s), {ReceiptCount} receipt(s), and {VerifiedPairCount} verified pair(s) are preserved."
            : BuildFirstJourneyStatus();

        OnPropertyChanged(nameof(ReceiptCount));
        OnPropertyChanged(nameof(OpenableEvidenceCount));
        OnPropertyChanged(nameof(HasSelectedEvidence));
    }

    private string BuildFirstJourneyStatus()
    {
        var next = FirstJourneyRequirements.FirstOrDefault(item => !item.IsCompleted);
        return next is null
            ? "Evidence Center is ready."
            : $"Next requirement: {next.Title} — {next.Description}";
    }

    private void ReplaceFirstJourneyRequirements(IEnumerable<EvidenceJourneyRequirement> values)
    {
        FirstJourneyRequirements.Clear();
        foreach (var value in values)
            FirstJourneyRequirements.Add(value);
    }
}

public sealed record EvidenceJourneyRequirement(string Title, string Description, bool IsCompleted)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
}
