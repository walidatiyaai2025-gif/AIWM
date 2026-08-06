using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class WordPressExplorerViewModel
{
    public ObservableCollection<ExplorerJourneyRequirement> FirstJourneyRequirements { get; } = [];

    private bool _isFirstJourneyReady;
    private string _firstJourneyStatus = "Select a connected site, then load or synchronize its local WordPress snapshot.";

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

    public IAsyncRelayCommand EnsureSnapshotCommand => RefreshCommand;

    internal void RefreshFirstJourneyReadiness()
    {
        var hasSite = HasSelectedSite;
        var hasSnapshot = LoadedAt.HasValue && LoadedAt.Value != DateTimeOffset.MinValue;
        var hasContent = TotalPosts > 0 || TotalPages > 0;
        var hasTaxonomy = TotalCategories > 0 || TotalTags > 0;
        var hasMediaState = TotalMedia >= 0 && hasSnapshot;

        FirstJourneyRequirements.Clear();
        FirstJourneyRequirements.Add(new ExplorerJourneyRequirement("Connected site context", hasSite,
            hasSite ? SelectedSiteName : "Select and verify a site in Sites."));
        FirstJourneyRequirements.Add(new ExplorerJourneyRequirement("SQLite snapshot", hasSnapshot,
            hasSnapshot ? $"Loaded {LoadedAt!.Value.LocalDateTime:g}" : "Run Synchronize now to create the first snapshot."));
        FirstJourneyRequirements.Add(new ExplorerJourneyRequirement("Posts or pages", hasContent,
            hasContent ? $"{TotalPosts} posts and {TotalPages} pages cached." : "No posts or pages are cached yet."));
        FirstJourneyRequirements.Add(new ExplorerJourneyRequirement("Categories or tags", hasTaxonomy,
            hasTaxonomy ? $"{TotalCategories} categories and {TotalTags} tags cached." : "No taxonomy data is cached yet."));
        FirstJourneyRequirements.Add(new ExplorerJourneyRequirement("Media inventory checked", hasMediaState,
            hasSnapshot ? $"{TotalMedia} media items recorded; zero is accepted." : "Media inventory has not been checked."));

        IsFirstJourneyReady = hasSite && hasSnapshot && hasContent && hasTaxonomy && hasMediaState;
        FirstJourneyStatus = IsFirstJourneyReady
            ? "WordPress snapshot is complete and ready for the first SEO Audit."
            : IsLoading
                ? $"Synchronization in progress: {CurrentOperation} ({ProgressPercent}%)."
                : "Complete every snapshot requirement before continuing to SEO Audit.";
    }
}

public sealed record ExplorerJourneyRequirement(string Title, bool IsCompleted, string Detail)
{
    public string StatusIcon => IsCompleted ? "✓" : "○";
}
