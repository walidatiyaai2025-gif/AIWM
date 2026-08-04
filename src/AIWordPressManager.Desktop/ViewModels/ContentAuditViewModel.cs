using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using AIWordPressManager.Application.ContentAudit;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ContentAuditViewModel : ObservableObject
{
    private readonly IContentAuditService _service;
    private readonly SitesViewModel _sites;

    public ObservableCollection<ContentAuditIssueDto> Issues { get; } = [];
    public ICollectionView IssuesView { get; }
    public ObservableCollection<string> SeverityOptions { get; } = ["All", "High", "Medium", "Low"];
    public ObservableCollection<string> TypeOptions { get; } = ["All", "Post", "Page"];

    public IAsyncRelayCommand RunAuditCommand { get; }
    public IRelayCommand ClearFiltersCommand { get; }
    public IRelayCommand OpenSelectedCommand { get; }
    public IRelayCommand CopyLinkCommand { get; }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _score;
    [ObservableProperty] private int _auditedItems;
    [ObservableProperty] private int _highIssues;
    [ObservableProperty] private int _mediumIssues;
    [ObservableProperty] private int _lowIssues;
    [ObservableProperty] private int _visibleIssues;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedSeverity = "All";
    [ObservableProperty] private string _selectedType = "All";
    [ObservableProperty] private ContentAuditIssueDto? _selectedIssue;
    [ObservableProperty] private string _lastCompleted = "Not run yet";
    [ObservableProperty] private string _statusMessage = "Synchronize a site, then run the measurable content audit.";

    public bool HasSelection => SelectedIssue is not null;
    public string SelectedIssueTitle => SelectedIssue?.ContentTitle ?? "Select an issue to inspect it.";
    public string SelectedIssueDetails => SelectedIssue is null
        ? "The issue code, description, WordPress ID, and page link will appear here."
        : $"{SelectedIssue.Code} • {SelectedIssue.Severity} • {SelectedIssue.ContentType} #{SelectedIssue.WordPressId}\n\n{SelectedIssue.Description}";
    public string SelectedIssueLink => SelectedIssue?.Link ?? "No link selected";

    public ContentAuditViewModel(IContentAuditService service, SitesViewModel sites)
    {
        _service = service;
        _sites = sites;

        IssuesView = CollectionViewSource.GetDefaultView(Issues);
        IssuesView.Filter = FilterIssue;

        RunAuditCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _sites.SelectedSite is not null);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedIssue is not null && Uri.TryCreate(SelectedIssue.Link, UriKind.Absolute, out _));
        CopyLinkCommand = new RelayCommand(CopySelectedLink, () => SelectedIssue is not null && !string.IsNullOrWhiteSpace(SelectedIssue.Link));

        _sites.SelectedSiteChanged += (_, _) =>
        {
            RunAuditCommand.NotifyCanExecuteChanged();
            SelectedIssue = null;
        };
    }

    partial void OnIsRunningChanged(bool value) => RunAuditCommand.NotifyCanExecuteChanged();
    partial void OnSearchTextChanged(string value) => RefreshFilters();
    partial void OnSelectedSeverityChanged(string value) => RefreshFilters();
    partial void OnSelectedTypeChanged(string value) => RefreshFilters();
    partial void OnSelectedIssueChanged(ContentAuditIssueDto? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedIssueTitle));
        OnPropertyChanged(nameof(SelectedIssueDetails));
        OnPropertyChanged(nameof(SelectedIssueLink));
        OpenSelectedCommand.NotifyCanExecuteChanged();
        CopyLinkCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site to load its saved content audit.";
            return;
        }

        var result = await _service.LoadLatestAsync(site.Id);
        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        Apply(result.Value, result.Value.CompletedAt == DateTimeOffset.MinValue
            ? "No saved content audit exists yet."
            : $"Loaded saved content audit from SQLite ({result.Value.CompletedAt.LocalDateTime:g}).");
    }

    private async Task RunAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;

        IsRunning = true;
        StatusMessage = "Auditing synchronized posts and pages…";
        try
        {
            var result = await _service.RunAsync(site.Id);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Apply(result.Value, $"Audit completed with {result.Value.Issues.Count} measurable issues.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Apply(ContentAuditSummary summary, string message)
    {
        Score = summary.Score;
        AuditedItems = summary.AuditedItems;
        HighIssues = summary.HighIssues;
        MediumIssues = summary.MediumIssues;
        LowIssues = summary.LowIssues;
        LastCompleted = summary.CompletedAt == DateTimeOffset.MinValue
            ? "Not run yet"
            : summary.CompletedAt.LocalDateTime.ToString("g");

        SelectedIssue = null;
        Issues.Clear();
        foreach (var issue in summary.Issues)
            Issues.Add(issue);

        IssuesView.Refresh();
        UpdateVisibleCount();
        StatusMessage = message;
    }

    private bool FilterIssue(object item)
    {
        if (item is not ContentAuditIssueDto issue)
            return false;

        if (!string.Equals(SelectedSeverity, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.Severity, SelectedSeverity, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(SelectedType, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(issue.ContentType, SelectedType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var term = SearchText.Trim();
        return issue.ContentTitle.Contains(term, StringComparison.OrdinalIgnoreCase)
               || issue.Description.Contains(term, StringComparison.OrdinalIgnoreCase)
               || issue.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
               || issue.ContentType.Contains(term, StringComparison.OrdinalIgnoreCase)
               || issue.Severity.Contains(term, StringComparison.OrdinalIgnoreCase)
               || issue.WordPressId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFilters()
    {
        IssuesView.Refresh();
        UpdateVisibleCount();
    }

    private void UpdateVisibleCount() => VisibleIssues = IssuesView.Cast<object>().Count();

    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedSeverity = "All";
        SelectedType = "All";
    }

    private void OpenSelected()
    {
        if (SelectedIssue is null || !Uri.TryCreate(SelectedIssue.Link, UriKind.Absolute, out var uri))
            return;

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void CopySelectedLink()
    {
        if (SelectedIssue is null || string.IsNullOrWhiteSpace(SelectedIssue.Link))
            return;

        Clipboard.SetText(SelectedIssue.Link);
        StatusMessage = "Selected content link copied to the clipboard.";
    }
}
