using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Deletion;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed record DeletionContentItem(
    string ContentType,
    int Id,
    string Title,
    string Slug,
    string Status,
    string Link,
    DateTimeOffset? ModifiedAt);

public sealed partial class DeletionCenterViewModel : ObservableObject
{
    private readonly IOfflineSnapshotService _offlineSnapshotService;
    private readonly IWordPressDeletionImpactStore _impactStore;
    private readonly IWordPressDeletionService _deletionService;
    private readonly IWordPressSynchronizationService _synchronizationService;
    private readonly SitesViewModel _sites;
    private readonly IDialogService _dialogService;

    public ObservableCollection<DeletionContentItem> ContentItems { get; } = [];
    public ObservableCollection<WordPressMediaItem> MediaItems { get; } = [];
    public ObservableCollection<DeletionContentItem> SelectedContentItems { get; } = [];
    public ObservableCollection<WordPressMediaItem> SelectedMediaItems { get; } = [];
    public ObservableCollection<MediaDeletionImpact> RelatedMedia { get; } = [];
    public ObservableCollection<string> MediaReferences { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand PreviewContentCommand { get; }
    public IAsyncRelayCommand PreviewMediaCommand { get; }
    public IAsyncRelayCommand MoveToTrashCommand { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
    public IAsyncRelayCommand DeleteContentPermanentlyCommand { get; }
    public IAsyncRelayCommand DeleteContentWithMediaCommand { get; }
    public IAsyncRelayCommand DeleteMediaPermanentlyCommand { get; }
    public IAsyncRelayCommand BulkMoveToTrashCommand { get; }
    public IAsyncRelayCommand BulkRestoreCommand { get; }
    public IAsyncRelayCommand BulkDeleteUnusedMediaCommand { get; }

    [ObservableProperty] private DeletionContentItem? _selectedContent;
    [ObservableProperty] private WordPressMediaItem? _selectedMedia;
    [ObservableProperty] private ContentDeletionPreview? _contentPreview;
    [ObservableProperty] private MediaDeletionImpact? _mediaPreview;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Select a post, page, or media item to preview the impact before any deletion.";
    [ObservableProperty] private string _restoreStatus = "draft";
    [ObservableProperty] private int _progressPercent;

    public IReadOnlyList<string> RestoreStatuses { get; } = ["draft", "publish", "pending", "private"];
    public bool HasSelectedSite => _sites.SelectedSite is not null;
    public bool HasContentPreview => ContentPreview is not null;
    public bool HasMediaPreview => MediaPreview is not null;
    public int SelectedContentCount => SelectedContentItems.Count;
    public int SelectedMediaCount => SelectedMediaItems.Count;

    public DeletionCenterViewModel(
        IOfflineSnapshotService offlineSnapshotService,
        IWordPressDeletionImpactStore impactStore,
        IWordPressDeletionService deletionService,
        IWordPressSynchronizationService synchronizationService,
        SitesViewModel sites,
        IDialogService dialogService)
    {
        _offlineSnapshotService = offlineSnapshotService;
        _impactStore = impactStore;
        _deletionService = deletionService;
        _synchronizationService = synchronizationService;
        _sites = sites;
        _dialogService = dialogService;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && HasSelectedSite);
        PreviewContentCommand = new AsyncRelayCommand(PreviewContentAsync, () => !IsBusy && SelectedContent is not null);
        PreviewMediaCommand = new AsyncRelayCommand(PreviewMediaAsync, () => !IsBusy && SelectedMedia is not null);
        MoveToTrashCommand = new AsyncRelayCommand(MoveToTrashAsync, () => !IsBusy && ContentPreview is not null);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !IsBusy && SelectedContent is not null);
        DeleteContentPermanentlyCommand = new AsyncRelayCommand(DeleteContentPermanentlyAsync, () => !IsBusy && ContentPreview is not null);
        DeleteContentWithMediaCommand = new AsyncRelayCommand(DeleteContentWithMediaAsync, () => !IsBusy && ContentPreview is not null);
        DeleteMediaPermanentlyCommand = new AsyncRelayCommand(DeleteMediaPermanentlyAsync, () => !IsBusy && MediaPreview is not null);
        BulkMoveToTrashCommand = new AsyncRelayCommand(BulkMoveToTrashAsync, () => !IsBusy && SelectedContentItems.Count > 0);
        BulkRestoreCommand = new AsyncRelayCommand(BulkRestoreAsync, () => !IsBusy && SelectedContentItems.Count > 0);
        BulkDeleteUnusedMediaCommand = new AsyncRelayCommand(BulkDeleteUnusedMediaAsync, () => !IsBusy && SelectedMediaItems.Count > 0);

        SelectedContentItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedContentCount));
            NotifyCommands();
        };
        SelectedMediaItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SelectedMediaCount));
            NotifyCommands();
        };

        _sites.SelectedSiteChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelectedSite));
            NotifyCommands();
        };
    }

    partial void OnSelectedContentChanged(DeletionContentItem? value)
    {
        ContentPreview = null;
        RelatedMedia.Clear();
        NotifyCommands();
    }

    partial void OnSelectedMediaChanged(WordPressMediaItem? value)
    {
        MediaPreview = null;
        MediaReferences.Clear();
        NotifyCommands();
    }

    partial void OnContentPreviewChanged(ContentDeletionPreview? value)
    {
        OnPropertyChanged(nameof(HasContentPreview));
        NotifyCommands();
    }

    partial void OnMediaPreviewChanged(MediaDeletionImpact? value)
    {
        OnPropertyChanged(nameof(HasMediaPreview));
        NotifyCommands();
    }

    partial void OnIsBusyChanged(bool value) => NotifyCommands();

    public async Task LoadAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        IsBusy = true;
        try
        {
            var snapshot = await _offlineSnapshotService.LoadAsync(site.Id);
            ContentItems.Clear();
            SelectedContentItems.Clear();
            foreach (var item in snapshot.Posts)
                ContentItems.Add(ToDeletionItem("post", item));
            foreach (var item in snapshot.Pages)
                ContentItems.Add(ToDeletionItem("page", item));

            MediaItems.Clear();
            SelectedMediaItems.Clear();
            foreach (var item in snapshot.Media)
                MediaItems.Add(item);

            StatusMessage = $"Offline safety snapshot loaded: {snapshot.Posts.Count} posts, {snapshot.Pages.Count} pages, and {snapshot.Media.Count} media items.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewContentAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null || SelectedContent is null)
            return;

        IsBusy = true;
        try
        {
            ContentPreview = await _impactStore.BuildPreviewAsync(
                site.Id,
                SelectedContent.ContentType,
                SelectedContent.Id);
            RelatedMedia.Clear();
            if (ContentPreview is not null)
            {
                foreach (var item in ContentPreview.RelatedMedia)
                    RelatedMedia.Add(item);
                StatusMessage = ContentPreview.Summary;
            }
            else
            {
                StatusMessage = "The selected content does not exist in the local snapshot.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewMediaAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null || SelectedMedia is null)
            return;

        IsBusy = true;
        try
        {
            MediaPreview = await _impactStore.BuildMediaPreviewAsync(site.Id, SelectedMedia.Id);
            MediaReferences.Clear();
            if (MediaPreview is not null)
            {
                foreach (var reference in MediaPreview.ReferencedBy)
                    MediaReferences.Add(reference);
                StatusMessage = MediaPreview.ReferenceCount == 0
                    ? "This media item is not referenced by synchronized posts or pages. Permanent deletion is eligible after backup and confirmation."
                    : $"Deletion blocked: this media item is referenced by {MediaPreview.ReferenceCount} content item(s).";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MoveToTrashAsync()
    {
        if (ContentPreview is null || _sites.SelectedSite is null)
            return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Move content to trash",
            $"Move '{ContentPreview.Target.Title}' to WordPress Trash?\n\nThe content can be restored later. Media will not be deleted.");
        if (!confirmed)
            return;

        await RunOperationAsync(() => _deletionService.MoveContentToTrashAsync(
            _sites.SelectedSite.Id,
            ContentPreview.Target.ContentType,
            ContentPreview.Target.WordPressId));
    }

    private async Task RestoreAsync()
    {
        if (SelectedContent is null || _sites.SelectedSite is null)
            return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Restore WordPress content",
            $"Restore '{SelectedContent.Title}' with status '{RestoreStatus}'?");
        if (!confirmed)
            return;

        await RunOperationAsync(() => _deletionService.RestoreContentAsync(
            _sites.SelectedSite.Id,
            SelectedContent.ContentType,
            SelectedContent.Id,
            RestoreStatus));
    }

    private async Task DeleteContentPermanentlyAsync()
    {
        if (ContentPreview is null || _sites.SelectedSite is null)
            return;

        var first = await _dialogService.ConfirmAsync(
            "Permanent deletion",
            $"Permanently delete '{ContentPreview.Target.Title}'?\n\nA JSON backup and local database backup will be created first. WordPress cannot restore this operation automatically.");
        if (!first)
            return;

        var second = await _dialogService.ConfirmAsync(
            "Final confirmation",
            $"This is irreversible on WordPress. Delete {ContentPreview.Target.ContentType} #{ContentPreview.Target.WordPressId} permanently?");
        if (!second)
            return;

        await RunOperationAsync(() => _deletionService.DeleteContentPermanentlyAsync(
            _sites.SelectedSite.Id,
            ContentPreview.Target.ContentType,
            ContentPreview.Target.WordPressId));
    }

    private async Task DeleteContentWithMediaAsync()
    {
        if (ContentPreview is null || _sites.SelectedSite is null)
            return;

        var message = $"Move '{ContentPreview.Target.Title}' to Trash and permanently delete {ContentPreview.ExclusiveMediaCount} exclusive media item(s)?\n\n" +
                      $"{ContentPreview.SharedMediaCount} shared media item(s) will be preserved. Every deleted media file is downloaded to Backups first.";
        var first = await _dialogService.ConfirmAsync("Content and media deletion", message);
        if (!first)
            return;

        var second = await _dialogService.ConfirmAsync(
            "Final media confirmation",
            "Media deletion is permanent in WordPress. Continue with backup and deletion of exclusive media only?");
        if (!second)
            return;

        await RunOperationAsync(() => _deletionService.DeleteContentAndExclusiveMediaAsync(
            _sites.SelectedSite.Id,
            ContentPreview.Target.ContentType,
            ContentPreview.Target.WordPressId));
    }

    private async Task DeleteMediaPermanentlyAsync()
    {
        if (MediaPreview is null || _sites.SelectedSite is null)
            return;

        if (MediaPreview.ReferenceCount > 0)
        {
            await _dialogService.ShowErrorAsync(
                "Media is still in use",
                $"Media #{MediaPreview.WordPressId} is referenced by {MediaPreview.ReferenceCount} synchronized content item(s). Remove those references before deletion.");
            return;
        }

        var first = await _dialogService.ConfirmAsync(
            "Permanent media deletion",
            $"Back up and permanently delete '{MediaPreview.Title}' from the WordPress Media Library?");
        if (!first)
            return;

        var second = await _dialogService.ConfirmAsync(
            "Final confirmation",
            $"Delete media #{MediaPreview.WordPressId} permanently? This cannot be restored by WordPress Trash.");
        if (!second)
            return;

        await RunOperationAsync(() => _deletionService.DeleteMediaPermanentlyAsync(
            _sites.SelectedSite.Id,
            MediaPreview.WordPressId));
    }


    private async Task BulkMoveToTrashAsync()
    {
        var site = _sites.SelectedSite;
        var selected = SelectedContentItems.ToArray();
        if (site is null || selected.Length == 0)
            return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Bulk move to Trash",
            $"Move {selected.Length} selected posts/pages to WordPress Trash?\n\nMedia will not be deleted. Every item can be restored later.");
        if (!confirmed)
            return;

        await RunBulkContentOperationAsync(selected, item =>
            _deletionService.MoveContentToTrashAsync(site.Id, item.ContentType, item.Id), "Moved to Trash");
    }

    private async Task BulkRestoreAsync()
    {
        var site = _sites.SelectedSite;
        var selected = SelectedContentItems.ToArray();
        if (site is null || selected.Length == 0)
            return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Bulk restore",
            $"Restore {selected.Length} selected posts/pages with status '{RestoreStatus}'?");
        if (!confirmed)
            return;

        await RunBulkContentOperationAsync(selected, item =>
            _deletionService.RestoreContentAsync(site.Id, item.ContentType, item.Id, RestoreStatus), "Restored");
    }

    private async Task BulkDeleteUnusedMediaAsync()
    {
        var site = _sites.SelectedSite;
        var selected = SelectedMediaItems.ToArray();
        if (site is null || selected.Length == 0)
            return;

        var previews = new List<MediaDeletionImpact>();
        foreach (var item in selected)
        {
            var preview = await _impactStore.BuildMediaPreviewAsync(site.Id, item.Id);
            if (preview is not null)
                previews.Add(preview);
        }

        var eligible = previews.Where(x => x.ReferenceCount == 0).ToArray();
        var blocked = previews.Where(x => x.ReferenceCount > 0).ToArray();
        if (eligible.Length == 0)
        {
            await _dialogService.ShowErrorAsync("No media eligible", $"All {blocked.Length} selected media items are still referenced by synchronized content.");
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Bulk permanent media deletion",
            $"{eligible.Length} selected media item(s) are unused and eligible for backup + permanent deletion.\n{blocked.Length} shared item(s) will be skipped.\n\nContinue?");
        if (!confirmed)
            return;

        var final = await _dialogService.ConfirmAsync(
            "Final bulk media confirmation",
            "Media deletion is permanent in WordPress. Each eligible file will be backed up first. Continue?");
        if (!final)
            return;

        IsBusy = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            for (var index = 0; index < eligible.Length; index++)
            {
                var result = await _deletionService.DeleteMediaPermanentlyAsync(site.Id, eligible[index].WordPressId);
                if (result.IsSuccess) succeeded++; else failed++;
                ProgressPercent = (index + 1) * 80 / eligible.Length;
            }

            await _synchronizationService.SynchronizeAsync(site.Id, new Progress<WordPressSyncProgress>(x => ProgressPercent = 80 + x.Percent / 5));
            StatusMessage = $"Bulk media deletion completed: {succeeded} deleted, {failed} failed, {blocked.Length} shared item(s) skipped.";
            await LoadAsync();
            ProgressPercent = 100;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunBulkContentOperationAsync(
        IReadOnlyList<DeletionContentItem> selected,
        Func<DeletionContentItem, Task<Result<WordPressDeletionResult>>> operation,
        string actionName)
    {
        IsBusy = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            for (var index = 0; index < selected.Count; index++)
            {
                var result = await operation(selected[index]);
                if (result.IsSuccess) succeeded++; else failed++;
                ProgressPercent = (index + 1) * 80 / selected.Count;
            }

            if (_sites.SelectedSite is not null)
                await _synchronizationService.SynchronizeAsync(_sites.SelectedSite.Id, new Progress<WordPressSyncProgress>(x => ProgressPercent = 80 + x.Percent / 5));

            StatusMessage = $"{actionName}: {succeeded} succeeded, {failed} failed.";
            await LoadAsync();
            ProgressPercent = 100;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunOperationAsync(Func<Task<Result<WordPressDeletionResult>>> operation)
    {
        IsBusy = true;
        ProgressPercent = 10;
        try
        {
            var result = await operation();
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                await _dialogService.ShowErrorAsync("WordPress operation failed", result.Error.Message);
                return;
            }

            ProgressPercent = 60;
            StatusMessage = result.Value.Message;
            await _dialogService.ShowInformationAsync(
                "WordPress operation completed",
                string.IsNullOrWhiteSpace(result.Value.BackupPath)
                    ? result.Value.Message
                    : $"{result.Value.Message}\n\nBackup: {result.Value.BackupPath}");

            if (_sites.SelectedSite is not null)
            {
                var progress = new Progress<WordPressSyncProgress>(value =>
                    ProgressPercent = 60 + value.Percent * 40 / 100);
                await _synchronizationService.SynchronizeAsync(_sites.SelectedSite.Id, progress);
            }

            ProgressPercent = 100;
            ContentPreview = null;
            MediaPreview = null;
            RelatedMedia.Clear();
            MediaReferences.Clear();
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DeletionContentItem ToDeletionItem(string type, WordPressContentItem item) =>
        new(type, item.Id, item.Title, item.Slug, item.Status, item.Link, item.ModifiedAt);

    private void NotifyCommands()
    {
        LoadCommand.NotifyCanExecuteChanged();
        PreviewContentCommand.NotifyCanExecuteChanged();
        PreviewMediaCommand.NotifyCanExecuteChanged();
        MoveToTrashCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        DeleteContentPermanentlyCommand.NotifyCanExecuteChanged();
        DeleteContentWithMediaCommand.NotifyCanExecuteChanged();
        DeleteMediaPermanentlyCommand.NotifyCanExecuteChanged();
        BulkMoveToTrashCommand.NotifyCanExecuteChanged();
        BulkRestoreCommand.NotifyCanExecuteChanged();
        BulkDeleteUnusedMediaCommand.NotifyCanExecuteChanged();
    }
}
