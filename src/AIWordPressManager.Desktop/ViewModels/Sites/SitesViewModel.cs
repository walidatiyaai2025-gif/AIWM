using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Sites;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels.Sites;

public sealed partial class SitesViewModel : ObservableObject
{
    private readonly ISiteManagementService _siteManagementService;
    private readonly IWordPressConnectionTester _connectionTester;
    private readonly IDialogService _dialogService;
    private readonly ICurrentSiteContext _currentSiteContext;
    private CancellationTokenSource? _siteDetailsCancellation;
    private CancellationTokenSource? _selectionNotificationCancellation;

    public ObservableCollection<SiteCardViewModel> Sites { get; } = [];
    public ObservableCollection<SiteCardViewModel> FilteredSites { get; } = [];
    public ObservableCollection<string> StatusOptions { get; } = ["All statuses", "Connected", "Needs attention"];
    public AddSiteWizardViewModel Wizard { get; }

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand AddSiteCommand { get; }
    public IAsyncRelayCommand<SiteCardViewModel?> SelectSiteCommand { get; }
    public IAsyncRelayCommand RetestSelectedSiteCommand { get; }
    public IAsyncRelayCommand RetestAllSitesCommand { get; }
    public IAsyncRelayCommand DeleteSelectedSiteCommand { get; }
    public IRelayCommand OpenSelectedSiteCommand { get; }
    public IRelayCommand OpenWordPressAdminCommand { get; }
    public IRelayCommand CopySelectedUrlCommand { get; }
    public IRelayCommand ClearFiltersCommand { get; }

    public event EventHandler? SelectedSiteChanged;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = "Sites are loaded from the local SQLite database.";
    [ObservableProperty] private SiteCardViewModel? _selectedSite;
    [ObservableProperty] private SiteDetailsDto? _selectedSiteDetails;
    [ObservableProperty] private string _lastDiagnostics = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedStatusFilter = "All statuses";
    [ObservableProperty] private int _retestProgress;
    [ObservableProperty] private string _currentOperation = string.Empty;

    public bool HasSites => Sites.Count > 0;
    public bool HasVisibleSites => FilteredSites.Count > 0;
    public bool HasFilteredEmptyState => HasSites && !HasVisibleSites;
    public bool HasSelectedSite => SelectedSite is not null;
    public int TotalSites => Sites.Count;
    public int ConnectedSites => Sites.Count(x => x.IsConnected);
    public int AttentionSites => Sites.Count(x => x.NeedsAttention);
    public int FilteredCount => FilteredSites.Count;
    public string ResultsSummary => $"Showing {FilteredCount} of {TotalSites} site(s)";
    public string SelectedLastTestText => SelectedSiteDetails?.LastConnectionTestAtUtc?.ToLocalTime().ToString("g") ?? "Never";
    public string SelectedHomeUrl => string.IsNullOrWhiteSpace(SelectedSiteDetails?.HomeUrl)
        ? SelectedSiteDetails?.SiteUrl ?? SelectedSite?.SiteUrl ?? string.Empty
        : SelectedSiteDetails.HomeUrl!;

    public SitesViewModel(
        ISiteManagementService siteManagementService,
        IWordPressConnectionTester connectionTester,
        IDialogService dialogService,
        AddSiteWizardViewModel wizard,
        ICurrentSiteContext currentSiteContext)
    {
        _siteManagementService = siteManagementService;
        _connectionTester = connectionTester;
        _dialogService = dialogService;
        _currentSiteContext = currentSiteContext;
        Wizard = wizard;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        AddSiteCommand = new RelayCommand(Wizard.Open);
        SelectSiteCommand = new AsyncRelayCommand<SiteCardViewModel?>(SelectSiteAsync);
        RetestSelectedSiteCommand = new AsyncRelayCommand(RetestSelectedSiteAsync, () => HasSelectedSite && !IsTestingConnection);
        RetestAllSitesCommand = new AsyncRelayCommand(RetestAllSitesAsync, () => HasSites && !IsTestingConnection);
        DeleteSelectedSiteCommand = new AsyncRelayCommand(DeleteSelectedSiteAsync, () => HasSelectedSite && !IsTestingConnection);
        OpenSelectedSiteCommand = new RelayCommand(() => OpenUrl(SelectedHomeUrl), () => HasSelectedSite);
        OpenWordPressAdminCommand = new RelayCommand(() => OpenUrl(BuildAdminUrl()), () => HasSelectedSite);
        CopySelectedUrlCommand = new RelayCommand(CopySelectedUrl, () => HasSelectedSite);
        ClearFiltersCommand = new RelayCommand(ClearFilters);

        Wizard.SiteSaved += async (_, _) => await LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedSiteChanged(SiteCardViewModel? value)
    {
        foreach (var item in Sites)
            item.IsSelected = ReferenceEquals(item, value);

        OnPropertyChanged(nameof(HasSelectedSite));
        OnPropertyChanged(nameof(SelectedLastTestText));
        OnPropertyChanged(nameof(SelectedHomeUrl));
        NotifyCommandStates();
        _currentSiteContext.SetCurrentSite(value?.Id, value?.Name, value?.SiteUrl);
        QueueSelectedSiteChangedNotification();
    }

    partial void OnSelectedSiteDetailsChanged(SiteDetailsDto? value)
    {
        OnPropertyChanged(nameof(SelectedLastTestText));
        OnPropertyChanged(nameof(SelectedHomeUrl));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadCommand.NotifyCanExecuteChanged();
        ErrorMessage = string.Empty;
        StatusMessage = "Loading saved WordPress sites from SQLite…";
        try
        {
            var selectedId = SelectedSite?.Id;
            Sites.Clear();
            foreach (var site in await _siteManagementService.GetSitesAsync())
            {
                Sites.Add(new SiteCardViewModel(
                    site.Id,
                    site.Name,
                    site.SiteUrl,
                    site.ConnectionStatus,
                    site.LastConnectionTestAtUtc?.ToLocalTime().ToString("g") ?? "Never"));
            }

            ApplyFilters();
            RaiseStatistics();

            if (selectedId.HasValue)
            {
                var selected = Sites.FirstOrDefault(x => x.Id == selectedId.Value);
                if (selected is not null)
                    await SelectSiteAsync(selected);
                else
                    ClearSelection();
            }
            else if (Sites.Count > 0)
            {
                await SelectSiteAsync(Sites[0]);
            }

            StatusMessage = Sites.Count == 0
                ? "No WordPress sites are registered yet."
                : $"Loaded {Sites.Count} site(s) from the local database.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not load sites. " + ex.Message;
            StatusMessage = ErrorMessage;
        }
        finally
        {
            IsLoading = false;
            LoadCommand.NotifyCanExecuteChanged();
            NotifyCommandStates();
        }
    }

    private Task SelectSiteAsync(SiteCardViewModel? site)
    {
        if (site is null || ReferenceEquals(SelectedSite, site))
            return Task.CompletedTask;

        SelectedSite = site;
        SelectedSiteDetails = null;
        StatusMessage = $"{site.Name} selected. Loading its local details in the background…";
        StartSelectedSiteDetailsLoad(site);
        return Task.CompletedTask;
    }

    private void StartSelectedSiteDetailsLoad(SiteCardViewModel site)
    {
        _siteDetailsCancellation?.Cancel();
        _siteDetailsCancellation?.Dispose();
        _siteDetailsCancellation = new CancellationTokenSource();
        _ = LoadSelectedSiteDetailsAsync(site, _siteDetailsCancellation.Token);
    }

    private async Task LoadSelectedSiteDetailsAsync(SiteCardViewModel site, CancellationToken cancellationToken)
    {
        CurrentOperation = "Loading selected site details…";
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var details = await _siteManagementService.GetDetailsAsync(site.Id);
            cancellationToken.ThrowIfCancellationRequested();

            if (SelectedSite?.Id != site.Id)
                return;

            SelectedSiteDetails = details;
            StatusMessage = $"{site.Name} selected. Modules will load its SQLite snapshot when opened.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (SelectedSite?.Id == site.Id)
                ErrorMessage = "Could not load site details. " + ex.Message;
        }
        finally
        {
            if (SelectedSite?.Id == site.Id)
                CurrentOperation = string.Empty;
        }
    }

    private void QueueSelectedSiteChangedNotification()
    {
        _selectionNotificationCancellation?.Cancel();
        _selectionNotificationCancellation?.Dispose();
        _selectionNotificationCancellation = new CancellationTokenSource();
        _ = NotifySelectedSiteChangedAsync(_selectionNotificationCancellation.Token);
    }

    private async Task NotifySelectedSiteChangedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(120, cancellationToken);
            var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted)
                return;

            await dispatcher.InvokeAsync(
                () => SelectedSiteChanged?.Invoke(this, EventArgs.Empty),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RetestSelectedSiteAsync()
    {
        if (SelectedSite is null)
            return;

        await TestSiteAsync(SelectedSite, showResultDialog: true);
        var selectedId = SelectedSite.Id;
        await LoadAsync();
        var refreshed = Sites.FirstOrDefault(x => x.Id == selectedId);
        if (refreshed is not null)
            await SelectSiteAsync(refreshed);
    }

    private async Task RetestAllSitesAsync()
    {
        if (Sites.Count == 0)
            return;

        IsTestingConnection = true;
        RetestProgress = 0;
        ErrorMessage = string.Empty;
        NotifyCommandStates();
        var succeeded = 0;
        var failed = 0;
        try
        {
            var snapshot = Sites.ToList();
            for (var index = 0; index < snapshot.Count; index++)
            {
                CurrentOperation = $"Testing {snapshot[index].Name} ({index + 1} of {snapshot.Count})…";
                var success = await TestSiteAsync(snapshot[index], showResultDialog: false);
                if (success) succeeded++; else failed++;
                RetestProgress = (int)Math.Round(((index + 1d) / snapshot.Count) * 100d);
            }

            await LoadAsync();
            StatusMessage = $"Connection tests completed. Connected: {succeeded}; attention required: {failed}.";
            await _dialogService.ShowInformationAsync("Connection tests completed", StatusMessage);
        }
        finally
        {
            IsTestingConnection = false;
            CurrentOperation = string.Empty;
            RetestProgress = 0;
            NotifyCommandStates();
        }
    }

    private async Task<bool> TestSiteAsync(SiteCardViewModel site, bool showResultDialog)
    {
        IsTestingConnection = true;
        NotifyCommandStates();
        try
        {
            var connection = await _siteManagementService.GetConnectionDataAsync(site.Id);
            if (connection is null)
            {
                LastDiagnostics = "Saved credentials could not be found for this site.";
                if (showResultDialog)
                    await _dialogService.ShowErrorAsync("Connection test", LastDiagnostics);
                return false;
            }

            CurrentOperation = $"Testing WordPress REST API for {site.Name}…";
            var result = await _connectionTester.TestAsync(new WordPressConnectionRequest(
                connection.SiteUrl,
                connection.UserName,
                connection.ApplicationPassword));

            LastDiagnostics = result.Diagnostics ?? result.Message;
            await _siteManagementService.UpdateConnectionResultAsync(
                connection.SiteId,
                result.IsSuccess,
                result.HomeUrl,
                result.WordPressVersion,
                result.LanguageCode);

            if (showResultDialog)
            {
                if (result.IsSuccess)
                    await _dialogService.ShowInformationAsync("Connection test", result.Message);
                else
                    await _dialogService.ShowErrorAsync("Connection test", result.Message);
            }

            return result.IsSuccess;
        }
        catch (Exception ex)
        {
            LastDiagnostics = ex.ToString();
            if (showResultDialog)
                await _dialogService.ShowErrorAsync("Connection test", ex.Message);
            return false;
        }
        finally
        {
            IsTestingConnection = false;
            CurrentOperation = string.Empty;
            NotifyCommandStates();
        }
    }

    private async Task DeleteSelectedSiteAsync()
    {
        if (SelectedSite is null)
            return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Remove site",
            $"Remove {SelectedSite.Name} from this application? WordPress itself will not be changed.");
        if (!confirmed)
            return;

        var result = await _siteManagementService.DeleteAsync(SelectedSite.Id);
        if (result.IsFailure)
        {
            await _dialogService.ShowErrorAsync("Remove site", result.Error.Message);
            return;
        }

        ClearSelection();
        await LoadAsync();
    }

    private void ApplyFilters()
    {
        var query = (SearchText ?? string.Empty).Trim();
        var status = SelectedStatusFilter ?? "All statuses";

        var filtered = Sites.Where(site =>
        {
            var searchMatches = string.IsNullOrWhiteSpace(query)
                || site.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || site.SiteUrl.Contains(query, StringComparison.OrdinalIgnoreCase)
                || site.DisplayHost.Contains(query, StringComparison.OrdinalIgnoreCase)
                || site.Status.Contains(query, StringComparison.OrdinalIgnoreCase);

            var statusMatches = status switch
            {
                "Connected" => site.IsConnected,
                "Needs attention" => site.NeedsAttention,
                _ => true
            };

            return searchMatches && statusMatches;
        }).ToList();

        FilteredSites.Clear();
        foreach (var site in filtered)
            FilteredSites.Add(site);

        OnPropertyChanged(nameof(HasVisibleSites));
        OnPropertyChanged(nameof(HasFilteredEmptyState));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(ResultsSummary));
    }

    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedStatusFilter = "All statuses";
        ApplyFilters();
    }

    private void CopySelectedUrl()
    {
        if (string.IsNullOrWhiteSpace(SelectedHomeUrl))
            return;

        try
        {
            Clipboard.SetText(SelectedHomeUrl);
            StatusMessage = "Site URL copied to the clipboard.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not copy the URL. " + ex.Message;
        }
    }

    private string BuildAdminUrl()
    {
        if (string.IsNullOrWhiteSpace(SelectedHomeUrl))
            return string.Empty;
        return SelectedHomeUrl.TrimEnd('/') + "/wp-admin/";
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void ClearSelection()
    {
        _siteDetailsCancellation?.Cancel();
        _selectionNotificationCancellation?.Cancel();
        SelectedSite = null;
        SelectedSiteDetails = null;
    }

    private void RaiseStatistics()
    {
        OnPropertyChanged(nameof(HasSites));
        OnPropertyChanged(nameof(HasFilteredEmptyState));
        OnPropertyChanged(nameof(TotalSites));
        OnPropertyChanged(nameof(ConnectedSites));
        OnPropertyChanged(nameof(AttentionSites));
        OnPropertyChanged(nameof(FilteredCount));
        OnPropertyChanged(nameof(ResultsSummary));
    }

    private void NotifyCommandStates()
    {
        RetestSelectedSiteCommand.NotifyCanExecuteChanged();
        RetestAllSitesCommand.NotifyCanExecuteChanged();
        DeleteSelectedSiteCommand.NotifyCanExecuteChanged();
        OpenSelectedSiteCommand.NotifyCanExecuteChanged();
        OpenWordPressAdminCommand.NotifyCanExecuteChanged();
        CopySelectedUrlCommand.NotifyCanExecuteChanged();
    }
}
