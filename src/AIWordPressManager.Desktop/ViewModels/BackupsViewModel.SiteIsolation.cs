using AIWordPressManager.Desktop.Services.Sites;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class BackupsViewModel
{
    internal void HandleActiveSiteChanged(CurrentSiteChangedEventArgs args)
    {
        SelectedItem = null;
        ProgressPercent = IsBusy ? ProgressPercent : 0;

        if (IsBusy)
        {
            StatusMessage = args.Current.HasSite
                ? $"Active site changed to {args.Current.SiteName}. The current global SQLite backup operation may finish, but no restore remains selected."
                : "The active site was cleared. The current global SQLite backup operation may finish, but no restore remains selected.";
            return;
        }

        CurrentStep = "Review backup history before restore";
        StatusMessage = args.Current.HasSite
            ? $"{args.Current.SiteName} selected. Backup files protect the complete local database; select and verify a recovery point again before restore."
            : "No site is selected. Backup files protect the complete local database; select and verify a recovery point again before restore.";
    }
}
