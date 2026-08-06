namespace AIWordPressManager.Desktop.Services.Sites;

public sealed record SiteOperationLease(
    CurrentSiteSnapshot Snapshot,
    string OperationName,
    DateTime StartedAtUtc)
{
    public Guid SiteId => Snapshot.SiteId
        ?? throw new InvalidOperationException($"{OperationName} requires a selected site.");

    public string SiteName => Snapshot.SiteName;
    public string SiteUrl => Snapshot.SiteUrl;
}

public interface ISiteOperationGuard
{
    SiteOperationLease Begin(string operationName);
    bool IsCurrent(SiteOperationLease lease);
    void EnsureCurrent(SiteOperationLease lease);
}

public sealed class SiteOperationGuard(ICurrentSiteContext siteContext) : ISiteOperationGuard
{
    public SiteOperationLease Begin(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var snapshot = siteContext.Capture();
        if (!snapshot.HasSite)
            throw new InvalidOperationException($"{operationName} requires a selected WordPress site.");

        return new SiteOperationLease(snapshot, operationName.Trim(), DateTime.UtcNow);
    }

    public bool IsCurrent(SiteOperationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return siteContext.IsCurrent(lease.Snapshot);
    }

    public void EnsureCurrent(SiteOperationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (IsCurrent(lease)) return;

        var current = siteContext.Capture();
        throw new OperationCanceledException(
            $"{lease.OperationName} was cancelled because the active site changed " +
            $"from '{lease.SiteName}' to '{current.SiteName}'. No further WordPress action was allowed.");
    }
}
