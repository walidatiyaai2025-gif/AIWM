using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AIWordPressManager.Application.SeoAudit;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SeoAuditViewModel : ObservableObject
{
    private readonly ISeoAuditService _service;
    private readonly ICurrentSiteContext _siteContext;
    public ObservableCollection<SeoAuditIssueDto> Issues { get; } = [];
    public ObservableCollection<SeoAuditHistoryPoint> History { get; } = [];
    public IAsyncRelayCommand RunAuditCommand { get; }
    public IRelayCommand OpenSelectedCommand { get; }
    public IRelayCommand CopySelectedLinkCommand { get; }
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _auditedItems;
    [ObservableProperty] private int _highIssues;
    [ObservableProperty] private int _mediumIssues;
    [ObservableProperty] private int _lowIssues;
    [ObservableProperty] private SeoAuditIssueDto? _selectedIssue;
    [ObservableProperty] private string _statusMessage = "Synchronize a site, then run the measurable SEO audit.";

    public SeoAuditViewModel(ISeoAuditService service, ICurrentSiteContext siteContext)
    {
        _service = service;
        _siteContext = siteContext;
        RunAuditCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _siteContext.HasSite);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedIssue is not null && !string.IsNullOrWhiteSpace(SelectedIssue.Link));
        CopySelectedLinkCommand = new RelayCommand(CopySelectedLink, () => SelectedIssue is not null && !string.IsNullOrWhiteSpace(SelectedIssue.Link));
        _siteContext.CurrentSiteChanged += (_, args) =>
        {
            ClearResults();
            StatusMessage = args.Current.HasSite
                ? $"{args.Current.SiteName} selected. Load or run its SEO audit."
                : "Select a site, synchronize it, then run the measurable SEO audit.";
            RunAuditCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnIsRunningChanged(bool value) => RunAuditCommand.NotifyCanExecuteChanged();
    partial void OnSelectedIssueChanged(SeoAuditIssueDto? value)
    {
        OpenSelectedCommand.NotifyCanExecuteChanged();
        CopySelectedLinkCommand.NotifyCanExecuteChanged();
    }

    private void OpenSelected()
    {
        if (SelectedIssue is null || string.IsNullOrWhiteSpace(SelectedIssue.Link)) return;
        Process.Start(new ProcessStartInfo(SelectedIssue.Link) { UseShellExecute = true });
    }

    private void CopySelectedLink()
    {
        if (SelectedIssue is null || string.IsNullOrWhiteSpace(SelectedIssue.Link)) return;
        Clipboard.SetText(SelectedIssue.Link);
        StatusMessage = "Selected page link copied.";
    }

    public async Task LoadAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId)
        {
            StatusMessage = "Select a site to load its saved SEO audit.";
            return;
        }

        var result = await _service.LoadLatestAsync(siteId);
        if (!_siteContext.IsCurrent(context)) return;
        if (result.IsFailure) { StatusMessage = result.Error.Message; return; }
        Apply(result.Value, result.Value.CompletedAt == DateTimeOffset.MinValue
            ? "No saved SEO audit exists yet."
            : $"Loaded saved SEO audit from SQLite ({result.Value.CompletedAt.LocalDateTime:g}).");
        await LoadHistoryAsync(context);
    }

    private async Task RunAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId) return;
        IsRunning = true;
        StatusMessage = $"Running local measurable SEO checks for {context.SiteName}…";
        try
        {
            var result = await _service.RunAsync(siteId);
            if (!_siteContext.IsCurrent(context)) return;
            if (result.IsFailure) { StatusMessage = result.Error.Message; return; }
            Apply(result.Value, $"SEO audit completed with {result.Value.Issues.Count} measurable issues.");
            await LoadHistoryAsync(context);
        }
        finally { IsRunning = false; }
    }

    private async Task LoadHistoryAsync(CurrentSiteSnapshot context)
    {
        if (context.SiteId is not Guid siteId) return;
        var result = await _service.LoadHistoryAsync(siteId);
        if (!_siteContext.IsCurrent(context) || result.IsFailure) return;
        History.Clear();
        foreach (var point in result.Value.OrderBy(x => x.CapturedAt)) History.Add(point);
    }

    private void Apply(SeoAuditSummary summary, string message)
    {
        Score = summary.Score; AuditedItems = summary.AuditedItems; HighIssues = summary.HighIssues; MediumIssues = summary.MediumIssues; LowIssues = summary.LowIssues;
        Issues.Clear(); foreach (var issue in summary.Issues) Issues.Add(issue);
        StatusMessage = message;
    }

    private void ClearResults()
    {
        SelectedIssue = null;
        Score = AuditedItems = HighIssues = MediumIssues = LowIssues = 0;
        Issues.Clear();
        History.Clear();
    }
}
