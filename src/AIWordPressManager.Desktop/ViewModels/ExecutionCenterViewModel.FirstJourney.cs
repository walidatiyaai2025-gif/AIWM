using System.Collections.ObjectModel;
using System.IO;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ExecutionCenterViewModel
{
    public ObservableCollection<ExecutionJourneyRequirement> FirstJourneyRequirements { get; } = [];

    private bool _isFirstJourneyReady;
    private string _firstJourneyStatus = "Load the approved queue, execute a verified change, and preserve its receipt and evidence.";

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

    public bool HasTerminalExecutionState =>
        QueueState.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
        QueueState.Equals("Completed with failures", StringComparison.OrdinalIgnoreCase);

    public bool HasExecutionReceipt =>
        !string.IsNullOrWhiteSpace(LatestReceiptPath) && File.Exists(LatestReceiptPath);

    public bool HasExecutionEvidence =>
        (!string.IsNullOrWhiteSpace(BeforeEvidencePath) && File.Exists(BeforeEvidencePath)) ||
        (!string.IsNullOrWhiteSpace(AfterEvidencePath) && File.Exists(AfterEvidencePath)) ||
        EvidenceStatus.Contains("captured", StringComparison.OrdinalIgnoreCase) ||
        EvidenceStatus.Contains("saved", StringComparison.OrdinalIgnoreCase) ||
        EvidenceStatus.Contains("backup", StringComparison.OrdinalIgnoreCase);

    internal void RefreshFirstJourneyReadiness()
    {
        var hasSite = _sites.SelectedSite is not null;
        var queueLoaded = Items.Count > 0;
        var hasVerifiedExecution = ExecutedCount > 0 && LastExecutionUtc is not null;
        var terminalState = HasTerminalExecutionState;
        var receiptSaved = HasExecutionReceipt;
        var evidenceCaptured = HasExecutionEvidence;

        ReplaceFirstJourneyRequirements([
            new ExecutionJourneyRequirement("Approved queue loaded", "Execution Center loaded the selected site's approved changes from SQLite.", hasSite && queueLoaded),
            new ExecutionJourneyRequirement("Verified execution", "At least one approved WordPress change completed and was verified.", hasVerifiedExecution),
            new ExecutionJourneyRequirement("Terminal result", "The execution finished with a completed terminal state.", terminalState),
            new ExecutionJourneyRequirement("Execution receipt", "The final HTML/JSON audit receipt was saved locally.", receiptSaved),
            new ExecutionJourneyRequirement("Backup and evidence", "Before/after evidence or execution backup information is available.", evidenceCaptured)
        ]);

        IsFirstJourneyReady = hasSite && queueLoaded && hasVerifiedExecution && terminalState && receiptSaved && evidenceCaptured;
        FirstJourneyStatus = IsFirstJourneyReady
            ? $"Execution Center is complete. {ExecutedCount} change(s) executed and the receipt is ready for Evidence Center."
            : BuildFirstJourneyStatus();

        OnPropertyChanged(nameof(HasTerminalExecutionState));
        OnPropertyChanged(nameof(HasExecutionReceipt));
        OnPropertyChanged(nameof(HasExecutionEvidence));
    }

    private string BuildFirstJourneyStatus()
    {
        var next = FirstJourneyRequirements.FirstOrDefault(item => !item.IsCompleted);
        return next is null
            ? "Execution Center is ready."
            : $"Next requirement: {next.Title} — {next.Description}";
    }

    private void ReplaceFirstJourneyRequirements(IEnumerable<ExecutionJourneyRequirement> values)
    {
        FirstJourneyRequirements.Clear();
        foreach (var value in values)
            FirstJourneyRequirements.Add(value);
    }
}

public sealed record ExecutionJourneyRequirement(string Title, string Description, bool IsCompleted)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
}
