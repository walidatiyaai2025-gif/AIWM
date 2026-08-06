namespace AIWordPressManager.Desktop.Services.Sites;

public sealed class CurrentSiteContext : ICurrentSiteContext
{
    private readonly object _gate = new();
    private CurrentSiteSnapshot _snapshot = CurrentSiteSnapshot.Empty;

    public Guid? SiteId => Capture().SiteId;
    public string SiteName => Capture().SiteName;
    public string SiteUrl => Capture().SiteUrl;
    public bool HasSite => Capture().HasSite;
    public long Version => Capture().Version;
    public CurrentSiteSnapshot Snapshot => Capture();

    public event EventHandler<CurrentSiteChangedEventArgs>? CurrentSiteChanged;

    public CurrentSiteSnapshot Capture()
    {
        lock (_gate)
            return _snapshot;
    }

    public bool IsCurrent(CurrentSiteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = Capture();
        return current.Version == snapshot.Version
               && current.SiteId == snapshot.SiteId;
    }

    public void SetCurrentSite(Guid? siteId, string? siteName, string? siteUrl)
    {
        var normalizedName = string.IsNullOrWhiteSpace(siteName)
            ? "No site selected"
            : siteName.Trim();
        var normalizedUrl = siteUrl?.Trim() ?? string.Empty;

        CurrentSiteSnapshot previous;
        CurrentSiteSnapshot current;

        lock (_gate)
        {
            previous = _snapshot;
            if (previous.SiteId == siteId
                && string.Equals(previous.SiteName, normalizedName, StringComparison.Ordinal)
                && string.Equals(previous.SiteUrl, normalizedUrl, StringComparison.Ordinal))
            {
                return;
            }

            current = new CurrentSiteSnapshot(
                siteId,
                normalizedName,
                normalizedUrl,
                checked(previous.Version + 1),
                DateTime.UtcNow);
            _snapshot = current;
        }

        CurrentSiteChanged?.Invoke(this, new CurrentSiteChangedEventArgs(previous, current));
    }

    public void ClearCurrentSite() => SetCurrentSite(null, null, null);
}
