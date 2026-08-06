using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class WordPressExplorerViewModel : ObservableObject
{
    private readonly IWordPressSynchronizationService _sync;
    private readonly IOfflineSnapshotService _offline;
    private readonly ICurrentSiteContext _siteContext;
    private CancellationTokenSource? _cts;

    private IReadOnlyList<WordPressContentItem> _allPosts = [];
    private IReadOnlyList<WordPressContentItem> _allPages = [];
    private IReadOnlyList<WordPressCategoryItem> _allCategories = [];
    private IReadOnlyList<WordPressTagItem> _allTags = [];
    private IReadOnlyList<WordPressMediaItem> _allMedia = [];

    public ObservableCollection<WordPressContentItem> Posts { get; } = [];
    public ObservableCollection<WordPressContentItem> Pages { get; } = [];
    public ObservableCollection<WordPressCategoryItem> Categories { get; } = [];
    public ObservableCollection<WordPressTagItem> Tags { get; } = [];
    public ObservableCollection<WordPressMediaItem> Media { get; } = [];
    public ObservableCollection<string> Activity { get; } = [];
    public IReadOnlyList<string> StatusOptions { get; } = ["All statuses", "publish", "draft", "pending", "private", "future"];

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ClearFiltersCommand { get; }
    public IRelayCommand OpenSelectedContentCommand { get; }
    public IRelayCommand CopySelectedContentLinkCommand { get; }
    public IRelayCommand OpenSelectedMediaCommand { get; }
    public IRelayCommand CopySelectedMediaUrlCommand { get; }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "Select a site from Sites before opening WordPress Explorer.";
    [ObservableProperty] private DateTimeOffset? _loadedAt;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedStatus = "All statuses";
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private int _totalPosts;
    [ObservableProperty] private int _totalPages;
    [ObservableProperty] private int _totalCategories;
    [ObservableProperty] private int _totalTags;
    [ObservableProperty] private int _totalMedia;
    [ObservableProperty] private string _currentOperation = "Idle";
    [ObservableProperty] private WordPressContentItem? _selectedContent;
    [ObservableProperty] private WordPressMediaItem? _selectedMedia;
    [ObservableProperty] private string _lastSyncSummary = "No synchronization completed in this session.";

    public bool HasSelectedSite => _siteContext.HasSite;
    public string SelectedSiteName => _siteContext.SiteName;
    public bool CanCancel => IsLoading;
    public int LoadedItemsCount => Posts.Count + Pages.Count + Categories.Count + Tags.Count + Media.Count;
    public int FilteredPostsCount => Posts.Count;
    public int FilteredPagesCount => Pages.Count;
    public int FilteredCategoriesCount => Categories.Count;
    public int FilteredTagsCount => Tags.Count;
    public int FilteredMediaCount => Media.Count;
    public bool HasSelectedContent => SelectedContent is not null;
    public bool HasSelectedMedia => SelectedMedia is not null;
    public string SelectedContentPlainText => StripHtml(SelectedContent?.RenderedContent ?? string.Empty);
    public string SelectedContentSummary => string.IsNullOrWhiteSpace(SelectedContentPlainText)
        ? "No readable content is stored in the local snapshot."
        : SelectedContentPlainText;

    public WordPressExplorerViewModel(
        IWordPressSynchronizationService sync,
        IOfflineSnapshotService offline,
        ICurrentSiteContext siteContext)
    {
        _sync = sync;
        _offline = offline;
        _siteContext = siteContext;
        RefreshCommand = new AsyncRelayCommand(SynchronizeNowAsync, () => !IsLoading && HasSelectedSite);
        CancelCommand = new RelayCommand(Cancel, () => CanCancel);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        OpenSelectedContentCommand = new RelayCommand(OpenSelectedContent, () => HasSelectedContent);
        CopySelectedContentLinkCommand = new RelayCommand(CopySelectedContentLink, () => HasSelectedContent);
        OpenSelectedMediaCommand = new RelayCommand(OpenSelectedMedia, () => HasSelectedMedia);
        CopySelectedMediaUrlCommand = new RelayCommand(CopySelectedMediaUrl, () => HasSelectedMedia);
        _siteContext.CurrentSiteChanged += (_, args) =>
        {
            _cts?.Cancel();
            ClearSnapshot();
            OnPropertyChanged(nameof(HasSelectedSite));
            OnPropertyChanged(nameof(SelectedSiteName));
            RefreshCommand.NotifyCanExecuteChanged();
            StatusMessage = args.Current.HasSite
                ? $"{args.Current.SiteName} selected. Loading its local snapshot is ready."
                : "Select a site from Sites before loading WordPress content.";
        };
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedContentChanged(WordPressContentItem? value)
    {
        OnPropertyChanged(nameof(SelectedContentPlainText));
        OnPropertyChanged(nameof(SelectedContentSummary));
        OnPropertyChanged(nameof(HasSelectedContent));
        OpenSelectedContentCommand.NotifyCanExecuteChanged();
        CopySelectedContentLinkCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedMediaChanged(WordPressMediaItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedMedia));
        OpenSelectedMediaCommand.NotifyCanExecuteChanged();
        CopySelectedMediaUrlCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCancel));
        RefreshCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId)
        {
            StatusMessage = "Select a site from Sites before loading WordPress content.";
            return;
        }
        IsLoading = true;
        CurrentOperation = "Loading local snapshot";
        ProgressPercent = 5;
        try
        {
            var local = await _offline.LoadAsync(siteId);
            if (!_siteContext.IsCurrent(context)) return;
            ApplySnapshot(local);
            ProgressPercent = 100;
            CurrentOperation = "Offline data ready";
            StatusMessage = local.LoadedAt == DateTimeOffset.MinValue
                ? "No local snapshot exists yet. Use Synchronize now."
                : $"Offline snapshot loaded from SQLite. Last sync: {local.LoadedAt.LocalDateTime:g}.";
            AddActivity($"{DateTime.Now:t} • Offline snapshot loaded for {context.SiteName}");
        }
        finally { IsLoading = false; }
    }

    public async Task SynchronizeNowAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId)
        {
            StatusMessage = "Select a site first.";
            return;
        }
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsLoading = true;
        ProgressPercent = 10;
        CurrentOperation = "Connecting to WordPress";
        StatusMessage = $"Synchronizing WordPress content from {context.SiteName}…";
        AddActivity($"{DateTime.Now:t} • Synchronization started for {context.SiteName}");
        try
        {
            var progress = new Progress<WordPressSyncProgress>(value =>
            {
                if (!_siteContext.IsCurrent(context)) return;
                ProgressPercent = value.Percent;
                CurrentOperation = value.Step;
            });
            var result = await _sync.SynchronizeAsync(siteId, progress, _cts.Token);
            if (!_siteContext.IsCurrent(context)) return;
            if (result.IsFailure)
            {
                StatusMessage = $"Online sync failed. Offline data remains available. {result.Error.Message}";
                CurrentOperation = "Offline mode";
                AddActivity($"{DateTime.Now:t} • Synchronization failed: {result.Error.Message}");
                return;
            }
            ApplySnapshot(result.Value);
            var value = result.Value;
            ProgressPercent = 100;
            CurrentOperation = "Completed";
            LastSyncSummary =
                $"Inserted: {value.SyncSummary.ContentInserted + value.SyncSummary.CategoriesInserted + value.SyncSummary.TagsInserted + value.SyncSummary.MediaInserted} • " +
                $"Updated: {value.SyncSummary.ContentUpdated + value.SyncSummary.CategoriesUpdated + value.SyncSummary.TagsUpdated + value.SyncSummary.MediaUpdated} • " +
                $"Unavailable: {value.SyncSummary.ContentUnavailable + value.SyncSummary.CategoriesUnavailable + value.SyncSummary.TagsUnavailable + value.SyncSummary.MediaUnavailable}";
            StatusMessage = $"Synchronization completed. {value.Posts.Count} posts, {value.Pages.Count} pages, {value.Categories.Count} categories, {value.Tags.Count} tags and {value.Media.Count} media items are cached locally.";
            AddActivity($"{DateTime.Now:t} • {LastSyncSummary}");
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            if (_siteContext.IsCurrent(context))
            {
                CurrentOperation = "Cancelled";
                StatusMessage = "WordPress synchronization was cancelled.";
            }
        }
        finally { IsLoading = false; }
    }

    private void ApplySnapshot(WordPressExplorerSnapshot value)
    {
        _allPosts = value.Posts; _allPages = value.Pages; _allCategories = value.Categories; _allTags = value.Tags; _allMedia = value.Media;
        TotalPosts = value.TotalPosts; TotalPages = value.TotalPages; TotalCategories = value.TotalCategories; TotalTags = value.TotalTags; TotalMedia = value.TotalMedia;
        LoadedAt = value.LoadedAt;
        ApplyFilters();
    }

    private void ClearSnapshot()
    {
        _allPosts = []; _allPages = []; _allCategories = []; _allTags = []; _allMedia = [];
        TotalPosts = TotalPages = TotalCategories = TotalTags = TotalMedia = ProgressPercent = 0;
        LoadedAt = null;
        SelectedContent = null;
        SelectedMedia = null;
        LastSyncSummary = "No synchronization completed for the selected site in this session.";
        CurrentOperation = "Idle";
        ClearFilters();
    }

    private void Cancel() { _cts?.Cancel(); CurrentOperation = "Cancelling"; StatusMessage = "Cancelling WordPress synchronization…"; AddActivity($"{DateTime.Now:t} • Cancellation requested"); }
    private void ClearFilters() { SearchText = string.Empty; SelectedStatus = "All statuses"; ApplyFilters(); }

    private void ApplyFilters()
    {
        var query = SearchText.Trim();
        Replace(Posts, _allPosts.Where(item => Matches(item, query, SelectedStatus)));
        Replace(Pages, _allPages.Where(item => Matches(item, query, SelectedStatus)));
        Replace(Categories, _allCategories.Where(item => MatchesTerm(item.Name, item.Slug, query)));
        Replace(Tags, _allTags.Where(item => MatchesTerm(item.Name, item.Slug, query)));
        Replace(Media, _allMedia.Where(item => string.IsNullOrWhiteSpace(query) || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Slug.Contains(query, StringComparison.OrdinalIgnoreCase) || item.MimeType.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(LoadedItemsCount)); OnPropertyChanged(nameof(FilteredPostsCount)); OnPropertyChanged(nameof(FilteredPagesCount)); OnPropertyChanged(nameof(FilteredCategoriesCount)); OnPropertyChanged(nameof(FilteredTagsCount)); OnPropertyChanged(nameof(FilteredMediaCount));
    }

    private void OpenSelectedContent() => OpenUrl(SelectedContent?.Link, "Selected content does not contain a valid public URL.");
    private void CopySelectedContentLink() => CopyToClipboard(SelectedContent?.Link, "Content link");
    private void OpenSelectedMedia() => OpenUrl(SelectedMedia?.SourceUrl, "Selected media does not contain a valid source URL.");
    private void CopySelectedMediaUrl() => CopyToClipboard(SelectedMedia?.SourceUrl, "Media URL");

    private void OpenUrl(string? value, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out _)) { StatusMessage = missingMessage; return; }
        try { Process.Start(new ProcessStartInfo(value) { UseShellExecute = true }); StatusMessage = $"Opened {value}"; AddActivity($"{DateTime.Now:t} • Opened URL"); }
        catch (Exception exception) { StatusMessage = $"Could not open the URL: {exception.Message}"; }
    }

    private void CopyToClipboard(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) { StatusMessage = $"{label} is not available."; return; }
        try { Clipboard.SetText(value); StatusMessage = $"{label} copied to the clipboard."; }
        catch (Exception exception) { StatusMessage = $"Could not copy {label.ToLowerInvariant()}: {exception.Message}"; }
    }

    private static bool Matches(WordPressContentItem item, string query, string status) =>
        (status == "All statuses" || string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(query) || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Slug.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
    private static bool MatchesTerm(string name, string slug, string query) => string.IsNullOrWhiteSpace(query) || name.Contains(query, StringComparison.OrdinalIgnoreCase) || slug.Contains(query, StringComparison.OrdinalIgnoreCase);
    private void AddActivity(string message) { Activity.Insert(0, message); while (Activity.Count > 30) Activity.RemoveAt(Activity.Count - 1); }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private static string StripHtml(string value) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")).Replace("\r", " ").Replace("\n", " ").Trim();
}
