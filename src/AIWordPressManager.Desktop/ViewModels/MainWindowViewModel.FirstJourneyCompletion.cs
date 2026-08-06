using System.IO;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IRelayCommand? _openCompletedJourneyReceiptCommand;
    private IAsyncRelayCommand? _refreshCompletedJourneyCommand;

    public bool IsFirstJourneyCompleted =>
        Sites.IsFirstJourneyReady &&
        Explorer.IsFirstJourneyReady &&
        SeoAudit.IsFirstJourneyReady &&
        SuggestedChanges.IsFirstJourneyReady &&
        SuggestedChanges.IsApprovalJourneyReady &&
        ExecutionCenter.IsFirstJourneyReady &&
        EvidenceCenter.IsFirstJourneyReady;

    public string FirstJourneyCompletionTitle => IsFirstJourneyCompleted
        ? "First WordPress journey completed"
        : "First WordPress journey is still in progress";

    public string FirstJourneyCompletionSummary => IsFirstJourneyCompleted
        ? $"{Sites.SelectedSite?.Name ?? "The selected site"} completed the controlled path from connection and synchronization through SEO analysis, approval, verified execution, receipt creation, and evidence review."
        : FirstJourneySidebarSummary;

    public string FirstJourneyCompletionReceipt =>
        string.IsNullOrWhiteSpace(ExecutionCenter.LatestReceiptPath)
            ? "No final execution receipt is available yet."
            : ExecutionCenter.LatestReceiptPath;

    public string FirstJourneyCompletionEvidence => EvidenceCenter.IsFirstJourneyReady
        ? $"{EvidenceCenter.TotalCount} artifact(s), {EvidenceCenter.ReceiptCount} receipt file(s), and {EvidenceCenter.VerifiedPairCount} verified before/after pair(s)."
        : EvidenceCenter.FirstJourneyStatus;

    public IRelayCommand OpenCompletedJourneyReceiptCommand =>
        _openCompletedJourneyReceiptCommand ??= new RelayCommand(
            () => ExecutionCenter.OpenLatestReceiptCommand.Execute(null),
            () => !string.IsNullOrWhiteSpace(ExecutionCenter.LatestReceiptPath) && File.Exists(ExecutionCenter.LatestReceiptPath));

    public IAsyncRelayCommand RefreshCompletedJourneyCommand =>
        _refreshCompletedJourneyCommand ??= new AsyncRelayCommand(RefreshCompletedJourneyAsync);

    internal void RefreshFirstJourneyCompletion()
    {
        OnPropertyChanged(nameof(IsFirstJourneyCompleted));
        OnPropertyChanged(nameof(FirstJourneyCompletionTitle));
        OnPropertyChanged(nameof(FirstJourneyCompletionSummary));
        OnPropertyChanged(nameof(FirstJourneyCompletionReceipt));
        OnPropertyChanged(nameof(FirstJourneyCompletionEvidence));
        _openCompletedJourneyReceiptCommand?.NotifyCanExecuteChanged();
    }

    private async Task RefreshCompletedJourneyAsync()
    {
        await EvidenceCenter.LoadAsync();
        EvidenceCenter.MergeExecutionReceipts();
        EvidenceCenter.RefreshFirstJourneyReadiness();
        ExecutionCenter.RefreshFirstJourneyReadiness();
        RefreshFirstJourneySidebar();
        RefreshFirstJourneyCompletion();
    }
}
