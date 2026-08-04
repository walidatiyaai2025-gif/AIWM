using System.Collections.ObjectModel;
using AIWordPressManager.Application.BrokenLinks;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class BrokenLinksViewModel : ObservableObject
{
    private readonly IBrokenLinkScanService _service;
    private readonly SitesViewModel _sites;
    private CancellationTokenSource? _cts;
    public ObservableCollection<BrokenLinkDto> Results { get; } = [];
    public IAsyncRelayCommand RunScanCommand { get; }
    public IRelayCommand CancelCommand { get; }
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private int _checkedLinks;
    [ObservableProperty] private int _brokenCount;
    [ObservableProperty] private int _redirectCount;
    [ObservableProperty] private int _healthyCount;
    [ObservableProperty] private string _statusMessage = "Synchronize a site, then scan links in the local content snapshot.";

    public BrokenLinksViewModel(IBrokenLinkScanService service, SitesViewModel sites)
    {
        _service = service;
        _sites = sites;
        RunScanCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _sites.SelectedSite is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
        _sites.SelectedSiteChanged += (_, _) => RunScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) { RunScanCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); }

    public async Task LoadAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;
        var result = await _service.LoadLatestAsync(site.Id);
        if (result.IsFailure) { StatusMessage = result.Error.Message; return; }
        Apply(result.Value);
        StatusMessage = result.Value.CompletedAt == DateTimeOffset.MinValue
            ? "No saved broken-link scan exists yet."
            : $"Loaded saved broken-link results from SQLite ({result.Value.CompletedAt.LocalDateTime:g}).";
    }

    private async Task RunAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null) return;
        _cts?.Dispose(); _cts = new CancellationTokenSource();
        IsRunning = true; ProgressPercent = 0; StatusMessage = "Preparing link scan…";
        Results.Clear();
        try
        {
            var progress = new Progress<BrokenLinkScanProgress>(p => { ProgressPercent = p.Percent; StatusMessage = p.CurrentStep; });
            var result = await _service.RunAsync(site.Id, progress, _cts.Token);
            if (result.IsFailure) { StatusMessage = result.Error.Message; return; }
            Apply(result.Value);
            StatusMessage = $"Scan complete. {BrokenCount} broken/error link(s), {RedirectCount} redirect(s).";
        }
        catch (OperationCanceledException) { StatusMessage = "Link scan cancelled."; }
        finally { IsRunning = false; _cts.Dispose(); _cts = null; }
    }

    private void Apply(BrokenLinkScanSummary summary)
    {
        CheckedLinks = summary.CheckedLinks; BrokenCount = summary.BrokenLinks; RedirectCount = summary.Redirects; HealthyCount = summary.HealthyLinks;
        Results.Clear(); foreach (var item in summary.Results) Results.Add(item);
    }
}
