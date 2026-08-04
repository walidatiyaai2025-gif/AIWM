namespace AIWordPressManager.Desktop.Services.Sites;

public sealed class CurrentSiteContext : ICurrentSiteContext
{
    public Guid? SiteId { get; private set; }
    public string SiteName { get; private set; } = "No site selected";
    public string SiteUrl { get; private set; } = string.Empty;
    public bool HasSite => SiteId.HasValue;

    public event EventHandler? CurrentSiteChanged;

    public void SetCurrentSite(Guid? siteId, string? siteName, string? siteUrl)
    {
        var normalizedName = string.IsNullOrWhiteSpace(siteName) ? "No site selected" : siteName.Trim();
        var normalizedUrl = siteUrl?.Trim() ?? string.Empty;

        if (SiteId == siteId
            && string.Equals(SiteName, normalizedName, StringComparison.Ordinal)
            && string.Equals(SiteUrl, normalizedUrl, StringComparison.Ordinal))
        {
            return;
        }

        SiteId = siteId;
        SiteName = normalizedName;
        SiteUrl = normalizedUrl;
        CurrentSiteChanged?.Invoke(this, EventArgs.Empty);
    }
}
