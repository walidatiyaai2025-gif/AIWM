namespace AIWordPressManager.Desktop.Services.Sites;

public interface ICurrentSiteContext
{
    Guid? SiteId { get; }
    string SiteName { get; }
    string SiteUrl { get; }
    bool HasSite { get; }
    event EventHandler? CurrentSiteChanged;
    void SetCurrentSite(Guid? siteId, string? siteName, string? siteUrl);
}
