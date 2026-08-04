using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using AIWordPressManager.Automation.Visual;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Playwright;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class VisualInspectorViewModel : ObservableObject
{
    private readonly SitesViewModel _sites;
    private readonly VisualInspectionService _service;
    private readonly UiOperationService _operations;
    private readonly IDialogService _dialogs;
    private readonly ISuggestedChangeService _suggestions;

    public ObservableCollection<VisualInspectionResult> Results { get; } = [];
    public ObservableCollection<VisualInspectionRunSummary> History { get; } = [];
    public ObservableCollection<VisualViewportComparison> ComparisonRows { get; } = [];
    public IAsyncRelayCommand ScanCommand { get; }
    public IAsyncRelayCommand InstallBrowserCommand { get; }
    public IRelayCommand OpenScreenshotCommand { get; }
    public IAsyncRelayCommand ExportReportCommand { get; }
    public IAsyncRelayCommand CreateSuggestionsCommand { get; }

    [ObservableProperty] private VisualInspectionResult? _selectedResult;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusMessage = "Select a site and run the first responsive inspection.";
    [ObservableProperty] private int _totalIssues;
    [ObservableProperty] private string _lastRunText = "Never";
    [ObservableProperty] private string _trendText = "No previous run";
    [ObservableProperty] private string _comparisonSummary = "Run two inspections to create a baseline comparison.";
    [ObservableProperty] private bool _hasComparison;

    public VisualInspectorViewModel(SitesViewModel sites, VisualInspectionService service, UiOperationService operations, IDialogService dialogs, ISuggestedChangeService suggestions)
    {
        _sites = sites;
        _service = service;
        _operations = operations;
        _dialogs = dialogs;
        _suggestions = suggestions;
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning && _sites.SelectedSite is not null);
        InstallBrowserCommand = new AsyncRelayCommand(InstallBrowserAsync, () => !IsScanning);
        OpenScreenshotCommand = new RelayCommand(OpenScreenshot, () => SelectedResult is not null);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => Results.Count > 0);
        CreateSuggestionsCommand = new AsyncRelayCommand(CreateSuggestionsAsync, () => Results.Any(x => x.IssueCount > 0) && _sites.SelectedSite is not null);
        _sites.SelectedSiteChanged += (_, _) => ScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedResultChanged(VisualInspectionResult? value) => OpenScreenshotCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        ScanCommand.NotifyCanExecuteChanged();
        if (_sites.SelectedSite is null) return;
        Results.Clear();
        var saved = await _service.LoadLatestAsync(_sites.SelectedSite.SiteUrl);
        var history = await _service.LoadHistoryAsync(_sites.SelectedSite.SiteUrl);
        foreach (var result in saved) Results.Add(result);
        History.Clear();
        foreach (var item in history) History.Add(item);
        await LoadComparisonAsync();
        SelectedResult = Results.FirstOrDefault();
        TotalIssues = Results.Sum(x => x.IssueCount);
        LastRunText = history.FirstOrDefault()?.RunAtDisplay ?? (Results.Count > 0 ? "Loaded from disk" : "Never");
        UpdateTrend();
        StatusMessage = Results.Count > 0
            ? $"Loaded {Results.Count} saved viewports for {_sites.SelectedSite.Name} from local storage."
            : $"Ready to inspect {_sites.SelectedSite.Name}. No live request runs automatically.";
        ExportReportCommand.NotifyCanExecuteChanged();
        CreateSuggestionsCommand.NotifyCanExecuteChanged();
    }

    private async Task ScanAsync()
    {
        if (_sites.SelectedSite is null) return;
        IsScanning = true;
        ScanCommand.NotifyCanExecuteChanged();
        InstallBrowserCommand.NotifyCanExecuteChanged();
        Results.Clear();
        TotalIssues = 0;
        _operations.Start("Visual Inspector", "Starting", "Preparing responsive browser inspection", 2);
        try
        {
            var progress = new Progress<VisualInspectionProgress>(x => _operations.Report(x.Percent, x.Step, x.Detail));
            var results = await _service.InspectAsync(_sites.SelectedSite.SiteUrl, progress);
            foreach (var result in results) Results.Add(result);
            ExportReportCommand.NotifyCanExecuteChanged();
            SelectedResult = Results.FirstOrDefault();
            TotalIssues = Results.Sum(x => x.IssueCount);
            History.Clear();
            foreach (var item in await _service.LoadHistoryAsync(_sites.SelectedSite.SiteUrl)) History.Add(item);
            await LoadComparisonAsync();
            LastRunText = History.FirstOrDefault()?.RunAtDisplay ?? DateTime.Now.ToString("g");
            UpdateTrend();
            CreateSuggestionsCommand.NotifyCanExecuteChanged();
            StatusMessage = $"Captured {Results.Count} viewports for {_sites.SelectedSite.Name}. {TotalIssues} visual signals require review.";
            _operations.Complete(StatusMessage);
            await Task.Delay(700);
        }
        catch (PlaywrightException exception) when (exception.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
        {
            const string message = "Playwright Chromium is required for visual inspection.";
            StatusMessage = message;
            _operations.Fail(message);
            var install = await _dialogs.ConfirmAsync(
                "Visual Inspector",
                "Playwright Chromium is not installed. Install it now? The download runs once and may take several minutes.");
            if (install)
            {
                await InstallBrowserAsync();
            }
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _operations.Fail(exception.Message);
            await _dialogs.ShowErrorAsync("Visual Inspector", exception.Message);
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            InstallBrowserCommand.NotifyCanExecuteChanged();
            await Task.Delay(500);
            _operations.Hide();
        }
    }



    private async Task InstallBrowserAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanCommand.NotifyCanExecuteChanged();
        InstallBrowserCommand.NotifyCanExecuteChanged();
        _operations.Start("Visual Inspector", "Installing browser", "Preparing Playwright Chromium", 5);
        try
        {
            var progress = new Progress<VisualInspectionProgress>(x =>
                _operations.Report(x.Percent, x.Step, x.Detail));
            var result = await _service.InstallChromiumAsync(progress);
            StatusMessage = result.Message;
            if (!result.Success)
            {
                _operations.Fail(result.Message);
                await _dialogs.ShowErrorAsync("Visual Inspector", result.Message);
                return;
            }

            _operations.Complete("Playwright Chromium is ready.");
            await _dialogs.ShowInformationAsync(
                "Visual Inspector",
                "Playwright Chromium was installed successfully. You can run the visual inspection now.");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _operations.Fail(exception.Message);
            await _dialogs.ShowErrorAsync("Visual Inspector", exception.Message);
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            InstallBrowserCommand.NotifyCanExecuteChanged();
            await Task.Delay(500);
            _operations.Hide();
        }
    }

    private async Task CreateSuggestionsAsync()
    {
        if (_sites.SelectedSite is null || Results.Count == 0) return;
        _operations.Start("Visual Inspector", "Creating proposals", "Converting measured signals into reviewable changes", 10);
        try
        {
            var inputs = Results.Select(x => new VisualSuggestionInput(
                x.ViewportName, x.PageTitle, x.HorizontalOverflow, x.MissingAltImages,
                x.BrokenImages, x.SmallTextElements, x.SmallTouchTargets, x.ConsoleErrors)).ToArray();
            _operations.Report(55, "Saving proposals", "Writing non-destructive visual recommendations to SQLite");
            var result = await _suggestions.CreateFromVisualInspectionAsync(_sites.SelectedSite.Id, inputs);
            StatusMessage = $"Created {result.Created} visual suggestions; {result.Existing} already existed. Review them in Suggested Changes.";
            _operations.Complete(StatusMessage);
            await _dialogs.ShowInformationAsync("Visual Inspector", StatusMessage);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _operations.Fail(exception.Message);
            await _dialogs.ShowErrorAsync("Visual Inspector", exception.Message);
        }
        finally
        {
            await Task.Delay(500);
            _operations.Hide();
        }
    }



    private async Task LoadComparisonAsync()
    {
        ComparisonRows.Clear();
        HasComparison = false;
        ComparisonSummary = "Run two inspections to create a baseline comparison.";

        if (_sites.SelectedSite is null) return;

        var comparison = await _service.LoadLatestComparisonAsync(_sites.SelectedSite.SiteUrl);
        if (comparison is null) return;

        foreach (var row in comparison.Viewports)
            ComparisonRows.Add(row);

        HasComparison = ComparisonRows.Count > 0;
        ComparisonSummary = comparison.Summary;
    }


    private void UpdateTrend()
    {
        if (History.Count < 2)
        {
            TrendText = History.Count == 1 ? "Baseline saved" : "No previous run";
            return;
        }
        var delta = History[0].TotalIssues - History[1].TotalIssues;
        TrendText = delta switch
        {
            < 0 => $"Improved by {-delta} signals",
            > 0 => $"Increased by {delta} signals",
            _ => "No change"
        };
    }

    private async Task ExportReportAsync()
    {
        if (_sites.SelectedSite is null || Results.Count == 0) return;
        var file = await _service.ExportHtmlAsync(_sites.SelectedSite.SiteUrl, Results);
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        StatusMessage = $"Visual inspection report exported to {file}.";
    }

    private void OpenScreenshot()
    {
        if (SelectedResult is null || !File.Exists(SelectedResult.ScreenshotPath)) return;
        Process.Start(new ProcessStartInfo(SelectedResult.ScreenshotPath) { UseShellExecute = true });
    }
}
