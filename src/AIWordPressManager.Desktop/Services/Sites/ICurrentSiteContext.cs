namespace AIWordPressManager.Desktop.Services.Sites;

public sealed record CurrentSiteSnapshot(
    Guid? SiteId,
    string SiteName,
    string SiteUrl,
    long Version,
    DateTime ChangedAtUtc)
{
    public bool HasSite => SiteId.HasValue;

    public static CurrentSiteSnapshot Empty { get; } = new(
        null,
        "No site selected",
        string.Empty,
        0,
        DateTime.MinValue);
}

public sealed class CurrentSiteChangedEventArgs(
    CurrentSiteSnapshot previous,
    CurrentSiteSnapshot current) : EventArgs
{
    public CurrentSiteSnapshot Previous { get; } = previous;
    public CurrentSiteSnapshot Current { get; } = current;
}

public interface ICurrentSiteContext
{
    Guid? SiteId { get; }
    string SiteName { get; }
    string SiteUrl { get; }
    bool HasSite { get; }
    long Version { get; }
    CurrentSiteSnapshot Snapshot { get; }

    event EventHandler<CurrentSiteChangedEventArgs>? CurrentSiteChanged;

    CurrentSiteSnapshot Capture();
    bool IsCurrent(CurrentSiteSnapshot snapshot);
    void SetCurrentSite(Guid? siteId, string? siteName, string? siteUrl);
    void ClearCurrentSite();
}
