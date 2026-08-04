using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed record EditableContentListItem(string ContentType, int Id, string Title, string Status, string Slug);

public sealed partial class PostSeoEditorViewModel : ObservableObject
{
    private readonly IOfflineSnapshotService _offline;
    private readonly IWordPressPostEditorService _editor;
    private readonly IWordPressSynchronizationService _sync;
    private readonly SitesViewModel _sites;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _selectionLoadCts;

    public ObservableCollection<EditableContentListItem> Items { get; } = [];
    public ObservableCollection<EditableContentListItem> SelectedItems { get; } = [];
    public IReadOnlyList<string> Statuses { get; } = ["draft", "pending", "publish", "future", "private"];

    public IAsyncRelayCommand LoadOfflineCommand { get; }
    public IAsyncRelayCommand LoadLiveCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand BulkUpdateCommand { get; }

    [ObservableProperty] private EditableContentListItem? _selectedItem;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusMessage = "Offline data loads first. Select one item for full editing or multiple items for a safe bulk update.";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _slug = "";
    [ObservableProperty] private string _status = "draft";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _excerpt = "";
    [ObservableProperty] private int _featuredMediaId;
    [ObservableProperty] private string _categoryIds = "";
    [ObservableProperty] private string _tagIds = "";
    [ObservableProperty] private string _template = "";
    [ObservableProperty] private string _commentStatus = "closed";
    [ObservableProperty] private string _pingStatus = "closed";
    [ObservableProperty] private bool _sticky;
    [ObservableProperty] private DateTimeOffset? _dateGmt;
    [ObservableProperty] private int _seoScore;
    [ObservableProperty] private string _seoSummary = "No content loaded.";

    [ObservableProperty] private bool _bulkApplyStatus = true;
    [ObservableProperty] private string _bulkStatus = "draft";
    [ObservableProperty] private bool _bulkApplyCategories;
    [ObservableProperty] private string _bulkCategoryIds = "";
    [ObservableProperty] private bool _bulkApplyTags;
    [ObservableProperty] private string _bulkTagIds = "";
    [ObservableProperty] private bool _bulkApplyComments;
    [ObservableProperty] private string _bulkCommentStatus = "closed";
    [ObservableProperty] private bool _bulkApplyPings;
    [ObservableProperty] private string _bulkPingStatus = "closed";
    [ObservableProperty] private bool _bulkApplySticky;
    [ObservableProperty] private bool _bulkSticky;

    public int SelectedCount => SelectedItems.Count;

    public PostSeoEditorViewModel(
        IOfflineSnapshotService offline,
        IWordPressPostEditorService editor,
        IWordPressSynchronizationService sync,
        SitesViewModel sites,
        IDialogService dialogs)
    {
        _offline = offline;
        _editor = editor;
        _sync = sync;
        _sites = sites;
        _dialogs = dialogs;

        LoadOfflineCommand = new AsyncRelayCommand(LoadOfflineAsync, () => !IsBusy && _sites.SelectedSite is not null);
        LoadLiveCommand = new AsyncRelayCommand(LoadLiveAsync, () => !IsBusy && SelectedItem is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && SelectedItem is not null && !string.IsNullOrWhiteSpace(Title));
        BulkUpdateCommand = new AsyncRelayCommand(BulkUpdateAsync, () => !IsBusy && SelectedItems.Count > 0 && HasBulkFieldSelected());

        SelectedItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedCount));
            BulkUpdateCommand.NotifyCanExecuteChanged();
        };
        _sites.SelectedSiteChanged += (_, _) => Notify();
    }

    partial void OnSelectedItemChanged(EditableContentListItem? value)
    {
        Notify();
        _selectionLoadCts?.Cancel();
        _selectionLoadCts?.Dispose();
        _selectionLoadCts = null;

        if (value is null || _sites.SelectedSite is null)
            return;

        _selectionLoadCts = new CancellationTokenSource();
        _ = LoadSelectedItemAsync(value, _selectionLoadCts.Token);
    }
    partial void OnIsBusyChanged(bool value) => Notify();
    partial void OnTitleChanged(string value) => Analyze();
    partial void OnSlugChanged(string value) => Analyze();
    partial void OnExcerptChanged(string value) => Analyze();
    partial void OnContentChanged(string value) => Analyze();
    partial void OnBulkApplyStatusChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();
    partial void OnBulkApplyCategoriesChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();
    partial void OnBulkApplyTagsChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();
    partial void OnBulkApplyCommentsChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();
    partial void OnBulkApplyPingsChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();
    partial void OnBulkApplyStickyChanged(bool value) => BulkUpdateCommand.NotifyCanExecuteChanged();

    private bool HasBulkFieldSelected() =>
        BulkApplyStatus || BulkApplyCategories || BulkApplyTags || BulkApplyComments || BulkApplyPings || BulkApplySticky;

    private void Notify()
    {
        LoadOfflineCommand.NotifyCanExecuteChanged();
        LoadLiveCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        BulkUpdateCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadOfflineAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
            return;

        IsBusy = true;
        ProgressPercent = 10;
        try
        {
            var snapshot = await _offline.LoadAsync(site.Id);
            Items.Clear();
            SelectedItems.Clear();
            foreach (var item in snapshot.Posts)
                Items.Add(new("post", item.Id, item.Title, item.Status, item.Slug));
            foreach (var item in snapshot.Pages)
                Items.Add(new("page", item.Id, item.Title, item.Status, item.Slug));
            ProgressPercent = 100;
            StatusMessage = $"Loaded {Items.Count} posts and pages from SQLite. The screen remains usable while background sync runs.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadLiveAsync()
    {
        if (SelectedItem is null)
            return;

        await LoadSelectedItemAsync(SelectedItem, CancellationToken.None);
    }

    private async Task LoadSelectedItemAsync(EditableContentListItem item, CancellationToken cancellationToken)
    {
        var site = _sites.SelectedSite;
        if (site is null)
            return;

        try
        {
            await Task.Delay(180, cancellationToken);
            if (!ReferenceEquals(SelectedItem, item))
                return;

            IsBusy = true;
            ProgressPercent = 15;
            StatusMessage = $"Loading {item.ContentType} #{item.Id}...";

            var result = await _editor.GetAsync(site.Id, item.ContentType, item.Id, cancellationToken);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message.Contains("credentials", StringComparison.OrdinalIgnoreCase)
                    ? "The saved WordPress credential cannot be opened by this Windows profile. Open Sites and save the username/application password again."
                    : result.Error.Message;
                return;
            }

            if (!ReferenceEquals(SelectedItem, item))
                return;

            var value = result.Value;
            Title = value.Title;
            Slug = value.Slug;
            Status = value.Status;
            Content = value.Content;
            Excerpt = value.Excerpt;
            FeaturedMediaId = value.FeaturedMediaId;
            CategoryIds = string.Join(",", value.CategoryIds);
            TagIds = string.Join(",", value.TagIds);
            Template = value.Template;
            CommentStatus = value.CommentStatus;
            PingStatus = value.PingStatus;
            Sticky = value.Sticky;
            DateGmt = value.DateGmt;
            ProgressPercent = 100;
            StatusMessage = $"Live editable fields loaded automatically. Last modified: {value.ModifiedGmt?.LocalDateTime:g}.";
            Analyze();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load the selected item: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null || SelectedItem is null)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Update WordPress content",
            $"Update '{Title}' on WordPress?\n\nA local JSON snapshot and SQLite backup will be created before the update.");
        if (!confirmed)
            return;

        IsBusy = true;
        ProgressPercent = 15;
        try
        {
            var request = new WordPressContentUpdateRequest(
                SelectedItem.ContentType,
                SelectedItem.Id,
                Title,
                Slug,
                Status,
                Content,
                Excerpt,
                DateGmt,
                FeaturedMediaId,
                ParseIds(CategoryIds),
                ParseIds(TagIds),
                Template,
                CommentStatus,
                PingStatus,
                "standard",
                Sticky);

            var result = await _editor.UpdateAsync(site.Id, request);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            ProgressPercent = 60;
            StatusMessage = $"{result.Value.Message} Backup: {result.Value.BackupPath}";
            await _sync.SynchronizeAsync(site.Id, null);
            await LoadOfflineAsync();
            ProgressPercent = 100;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BulkUpdateAsync()
    {
        var site = _sites.SelectedSite;
        var selected = SelectedItems.ToArray();
        if (site is null || selected.Length == 0)
            return;

        var fields = new List<string>();
        if (BulkApplyStatus) fields.Add($"status → {BulkStatus}");
        if (BulkApplyCategories) fields.Add($"categories → {BulkCategoryIds}");
        if (BulkApplyTags) fields.Add($"tags → {BulkTagIds}");
        if (BulkApplyComments) fields.Add($"comments → {BulkCommentStatus}");
        if (BulkApplyPings) fields.Add($"pings → {BulkPingStatus}");
        if (BulkApplySticky) fields.Add($"sticky → {BulkSticky}");

        var confirmed = await _dialogs.ConfirmAsync(
            "Bulk WordPress update",
            $"Apply the following changes to {selected.Length} selected posts/pages?\n\n{string.Join("\n", fields)}\n\nEach item is backed up before update. The operation continues item-by-item and reports failures.");
        if (!confirmed)
            return;

        IsBusy = true;
        ProgressPercent = 0;
        var succeeded = 0;
        var failures = new List<string>();
        try
        {
            for (var index = 0; index < selected.Length; index++)
            {
                var item = selected[index];
                StatusMessage = $"Loading live fields for {index + 1}/{selected.Length}: {item.Title}";
                var liveResult = await _editor.GetAsync(site.Id, item.ContentType, item.Id);
                if (liveResult.IsFailure)
                {
                    failures.Add($"{item.Title}: {liveResult.Error.Message}");
                    ProgressPercent = (index + 1) * 100 / selected.Length;
                    continue;
                }

                var live = liveResult.Value;
                var request = new WordPressContentUpdateRequest(
                    item.ContentType,
                    item.Id,
                    live.Title,
                    live.Slug,
                    BulkApplyStatus ? BulkStatus : live.Status,
                    live.Content,
                    live.Excerpt,
                    live.DateGmt,
                    live.FeaturedMediaId,
                    BulkApplyCategories ? ParseIds(BulkCategoryIds) : live.CategoryIds,
                    BulkApplyTags ? ParseIds(BulkTagIds) : live.TagIds,
                    live.Template,
                    BulkApplyComments ? BulkCommentStatus : live.CommentStatus,
                    BulkApplyPings ? BulkPingStatus : live.PingStatus,
                    live.Format,
                    BulkApplySticky ? BulkSticky : live.Sticky);

                var updateResult = await _editor.UpdateAsync(site.Id, request);
                if (updateResult.IsSuccess)
                    succeeded++;
                else
                    failures.Add($"{item.Title}: {updateResult.Error.Message}");

                ProgressPercent = (index + 1) * 100 / selected.Length;
            }

            StatusMessage = failures.Count == 0
                ? $"Bulk update completed for {succeeded} item(s). Synchronizing the offline cache…"
                : $"Bulk update completed: {succeeded} succeeded, {failures.Count} failed. {string.Join(" | ", failures.Take(5))}";

            await _sync.SynchronizeAsync(site.Id, null);
            await LoadOfflineAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Analyze()
    {
        var plain = Regex.Replace(Content ?? "", "<[^>]+>", " ");
        var words = Regex.Matches(plain, @"\b[\p{L}\p{N}]+\b").Count;
        var score = 100;
        var notes = new List<string>();
        if (Title.Length < 10 || Title.Length > 65) { score -= 15; notes.Add("Title should usually be 10–65 characters."); }
        if (string.IsNullOrWhiteSpace(Slug) || Slug.Length > 75) { score -= 15; notes.Add("Use a concise descriptive slug."); }
        if (Excerpt.Length < 70 || Excerpt.Length > 170) { score -= 15; notes.Add("Excerpt/meta summary should usually be 70–170 characters."); }
        if (words < 300) { score -= 20; notes.Add("Content is thin (under 300 words)."); }
        if (!Regex.IsMatch(Content ?? "", "<h2[^>]*>", RegexOptions.IgnoreCase)) { score -= 10; notes.Add("Add descriptive H2 sections."); }
        if (!Regex.IsMatch(Content ?? "", "<a[^>]+href=", RegexOptions.IgnoreCase)) { score -= 10; notes.Add("Add useful internal links."); }
        if (Regex.IsMatch(Content ?? "", "<img(?![^>]*alt=[\"'][^\"']+)", RegexOptions.IgnoreCase)) { score -= 15; notes.Add("Some images appear to lack alt text."); }
        SeoScore = Math.Max(0, score);
        SeoSummary = notes.Count == 0
            ? "Good measurable on-page SEO baseline. Plugin-specific title, canonical, schema, and social metadata require their REST fields or the companion connector."
            : string.Join("\n", notes);
    }

    private static IReadOnlyList<int> ParseIds(string value) => value
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x, out var id) ? id : 0)
        .Where(x => x > 0)
        .Distinct()
        .ToArray();
}
