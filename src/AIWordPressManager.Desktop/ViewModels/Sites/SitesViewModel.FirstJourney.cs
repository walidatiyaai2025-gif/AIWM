using System.Collections.ObjectModel;
using System.Windows.Media;

namespace AIWordPressManager.Desktop.ViewModels.Sites;

public sealed partial class SitesViewModel
{
    public ObservableCollection<SiteJourneyRequirement> FirstJourneyRequirements { get; } = [];

    private string _firstJourneyStatus = "Register and verify one WordPress site to continue.";
    private bool _isFirstJourneyReady;

    public string FirstJourneyStatus
    {
        get => _firstJourneyStatus;
        private set => SetProperty(ref _firstJourneyStatus, value);
    }

    public bool IsFirstJourneyReady
    {
        get => _isFirstJourneyReady;
        private set => SetProperty(ref _isFirstJourneyReady, value);
    }

    internal void RefreshFirstJourneyReadiness()
    {
        var hasSavedSite = Sites.Count > 0;
        var hasSelectedSite = SelectedSite is not null;
        var connectionVerified = SelectedSite?.IsConnected == true;
        var detailsLoaded = SelectedSiteDetails is not null;

        FirstJourneyRequirements.Clear();
        FirstJourneyRequirements.Add(new SiteJourneyRequirement(
            "1", "Saved site", "A WordPress site is stored in the local SQLite database.", hasSavedSite));
        FirstJourneyRequirements.Add(new SiteJourneyRequirement(
            "2", "Active selection", "The site is selected as the current workspace context.", hasSelectedSite));
        FirstJourneyRequirements.Add(new SiteJourneyRequirement(
            "3", "Verified connection", "WordPress REST API credentials passed the connection test.", connectionVerified));
        FirstJourneyRequirements.Add(new SiteJourneyRequirement(
            "4", "Local details loaded", "The selected site's saved metadata is available offline.", detailsLoaded));

        IsFirstJourneyReady = hasSavedSite && hasSelectedSite && connectionVerified && detailsLoaded;
        FirstJourneyStatus = IsFirstJourneyReady
            ? "Sites page completed. Continue to WordPress Explorer for the first synchronization."
            : BuildFirstJourneyStatus(hasSavedSite, hasSelectedSite, connectionVerified, detailsLoaded);
    }

    private static string BuildFirstJourneyStatus(
        bool hasSavedSite,
        bool hasSelectedSite,
        bool connectionVerified,
        bool detailsLoaded)
    {
        if (!hasSavedSite)
            return "Add your first WordPress site using the guided wizard.";
        if (!hasSelectedSite)
            return "Select the site that will be used for the first journey.";
        if (!connectionVerified)
            return "Retest the selected site until the WordPress connection is successful.";
        if (!detailsLoaded)
            return "Loading the selected site's local details…";
        return "Sites page is ready.";
    }
}

public sealed record SiteJourneyRequirement(
    string Number,
    string Title,
    string Description,
    bool IsCompleted)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
    public Brush StatusBrush => IsCompleted ? Brushes.SeaGreen : Brushes.SlateGray;
}
