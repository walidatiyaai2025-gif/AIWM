using System.Collections.ObjectModel;
using AIWordPressManager.Application.BrokenLinks;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class BrokenLinksViewModel : ObservableObject
{
    private readonly IBrokenLinkScanService _service;
    private readonly ICurrentSiteContext _siteContext;
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

    public BrokenLinksViewModel(IBrokenLinkScanService service, ICurrentSiteContext siteContext)
    {
        _service = service;
        _siteContext = siteContext;
        RunScanCommand = new AsyncRelayCommand(RunAsync, () => !IsRunning && _siteContext.HasSite);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);

        _siteContext.CurrentSiteChanged += (_, args) =>
        {
            _cts?.Cancel();
            ClearResults();
            StatusMessage = args.Current.HasSite
                ? $"{args.Current.SiteName} selected. Load or run its broken-link scan."
                : "Select a site, synchronize it, then scan links in the local content snapshot.";
            RunScanCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnIsRunningChanged(bool value)
    {
        RunScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId)
        {
            StatusMessage = "Select a site to load its saved broken-link scan.";
            return;
        }

        var result = await _service.LoadLatestAsync(siteId);
        if (!_siteContext.IsCurrent(context)) return;
        if (result.IsFailure)
        {
            StatusMessage = result.Error.Message;
            return;
        }

        Apply(result.Value);
        StatusMessage = result.Value.CompletedAt == DateTimeOffset.MinValue
            ? "No saved broken-link scan exists yet."
            : $"Loaded saved broken-link results for {context.SiteName} from SQLite ({result.Value.CompletedAt.LocalDateTime:g}).";
    }

    private async Task RunAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressPercent = 0;
        StatusMessage = $"Preparing link scan for {context.SiteName}…";
        Results.Clear();

        try
        {
            var progress = new Progress<BrokenLinkScanProgress>(value =>
            {
                if (!_siteContext.IsCurrent(context)) return;
                ProgressPercent = value.Percent;
                StatusMessage = value.CurrentStep;
            });

            var result = await _service.RunAsync(siteId, progress, _cts.Token);
            if (!_siteContext.IsCurrent(context)) return;
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            Apply(result.Value);
            StatusMessage = $"Scan complete for {context.SiteName}. {BrokenCount} broken/error link(s), {RedirectCount} redirect(s).";
        }
        catch (OperationCanceledException)
        {
            if (_siteContext.IsCurrent(context)) StatusMessage = "Link scan cancelled.";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "Cancelling link scan…";
    }

    private void Apply(BrokenLinkScanSummary summary)
    {
        CheckedLinks = summary.CheckedLinks;
        BrokenCount = summary.BrokenLinks;
        RedirectCount = summary.Redirects;
        HealthyCount = summary.HealthyLinks;
        Results.Clear();
        foreach (var item in summary.Results) Results.Add(item);
    }

    private void ClearResults()
    {
        ProgressPercent = CheckedLinks = BrokenCount = RedirectCount = HealthyCount = 0;
        Results.Clear();
    }
}
