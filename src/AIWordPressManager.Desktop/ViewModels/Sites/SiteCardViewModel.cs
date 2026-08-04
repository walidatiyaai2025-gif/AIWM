using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.ViewModels.Sites;

public sealed partial class SiteCardViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Name { get; }
    public string SiteUrl { get; }
    public string Status { get; }
    public string LastTestText { get; }

    public bool IsConnected => string.Equals(Status, "Connected", StringComparison.OrdinalIgnoreCase);
    public bool NeedsAttention => !IsConnected;
    public string StatusIcon => IsConnected ? "●" : "!";
    public string StatusLabel => IsConnected ? "Connected" : "Needs attention";
    public string JourneyState => IsConnected ? "Ready for synchronization" : "Connection requires attention";
    public string RecommendedAction => IsConnected ? "Start or review synchronization" : "Retest WordPress connection";
    public string PrimaryActionText => IsConnected ? "Open workspace" : "Retest connection";
    public string FaviconUrl => SiteUrl.TrimEnd('/') + "/favicon.ico";
    public string LastActivityText => string.Equals(LastTestText, "Never", StringComparison.OrdinalIgnoreCase)
        ? "Not tested yet"
        : $"Last connection test: {LastTestText}";

    public string DisplayHost
    {
        get
        {
            if (Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri))
                return uri.Host;
            return SiteUrl;
        }
    }

    public string WorkspaceSummary =>
        $"{DisplayHost} • {StatusLabel} • {LastActivityText}";

    [ObservableProperty] private bool _isSelected;

    public SiteCardViewModel(Guid id, string name, string siteUrl, string status, string lastTestText)
    {
        Id = id;
        Name = name;
        SiteUrl = siteUrl;
        Status = status;
        LastTestText = lastTestText;
    }
}
