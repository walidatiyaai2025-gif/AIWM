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
    public string DisplayHost
    {
        get
        {
            if (Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri))
                return uri.Host;
            return SiteUrl;
        }
    }

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
