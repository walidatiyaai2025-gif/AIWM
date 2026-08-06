using AIWordPressManager.Desktop.Services.Sites;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ExecutionCenterViewModel
{
    internal void HandleActiveSiteChanged(CurrentSiteChangedEventArgs args)
    {
        _cts?.Cancel();

        Items.Clear();
        SelectedItems.Clear();
        SelectedItem = null;
        BeforeEvidencePath = null;
        AfterEvidencePath = null;
        ProgressPercent = 0;
        CurrentJobId = null;
        LastExecutionUtc = null;
        QueueState = args.Current.HasSite ? "Site changed" : "No site selected";
        CurrentStep = args.Current.HasSite
            ? $"Load the isolated execution queue for {args.Current.SiteName}."
            : "Select a site before loading approved changes.";
        EvidenceStatus = "Evidence was cleared because the active site changed.";
        StatusMessage = args.Current.HasSite
            ? $"Active site changed to {args.Current.SiteName}. Any running execution was cancelled before another WordPress action could start."
            : "The active site was cleared. Any running execution was cancelled.";

        BuildPreviewPipeline(null);
        RaiseCounts();
    }
}
