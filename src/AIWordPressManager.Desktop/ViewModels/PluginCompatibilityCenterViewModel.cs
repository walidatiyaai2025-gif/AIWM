using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class PluginCompatibilityCenterViewModel : ObservableObject
{
    private readonly ICurrentSiteContext _currentSite;
    private readonly IWordPressVisualCssService _visualCssService;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenVisualEditorCommand { get; }
    public event Action<string>? NavigationRequested;

    public ObservableCollection<PluginCompatibilityRow> Plugins { get; } = new();
    public ObservableCollection<BridgeDiagnosticRow> Checks { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Select a site, then run compatibility diagnostics.";
    [ObservableProperty] private string _bridgeVersion = "Unknown";
    [ObservableProperty] private string _wordPressVersion = "Unknown";
    [ObservableProperty] private string _phpVersion = "Unknown";
    [ObservableProperty] private string _theme = "Unknown";
    [ObservableProperty] private string _overallState = "NOT TESTED";
    [ObservableProperty] private int _detectedPlugins;
    [ObservableProperty] private int _compatiblePlugins;
    [ObservableProperty] private int _blockingIssues;
    [ObservableProperty] private DateTimeOffset? _lastTestedAtUtc;

    public string LastTestedText => LastTestedAtUtc is null
        ? "Never tested"
        : LastTestedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public PluginCompatibilityCenterViewModel(
        ICurrentSiteContext currentSite,
        IWordPressVisualCssService visualCssService)
    {
        _currentSite = currentSite;
        _visualCssService = visualCssService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _currentSite.HasSite);
        OpenVisualEditorCommand = new RelayCommand(() => NavigationRequested?.Invoke("Visual WordPress Editor"));
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();
    partial void OnLastTestedAtUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastTestedText));

    public Task LoadAsync()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        if (!_currentSite.HasSite)
        {
            Status = "Select a site before testing WordPress plugins and Bridge compatibility.";
            ResetSummary();
        }
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        if (!_currentSite.HasSite || IsBusy) return;

        IsBusy = true;
        Status = "Running authenticated WordPress, Bridge, plugin, theme, and permission diagnostics...";
        Plugins.Clear();
        Checks.Clear();
        try
        {
            var result = await _visualCssService.RunDiagnosticsAsync(_currentSite.SiteId!.Value);
            LastTestedAtUtc = DateTimeOffset.UtcNow;
            if (result.IsFailure)
            {
                OverallState = "BLOCKED";
                BlockingIssues = 1;
                Status = "Diagnostics failed: " + result.Error.Message;
                return;
            }

            var report = result.Value;
            BridgeVersion = Empty(report.PluginVersion);
            WordPressVersion = Empty(report.WordPressVersion);
            PhpVersion = Empty(report.PhpVersion);
            Theme = string.Join(" / ", new[] { report.ActiveTheme, report.ActiveStylesheet }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(Theme)) Theme = "Unknown";

            AddPlugin("AI WordPress Manager Bridge", true, report.IsReady, report.PluginVersion,
                report.IsReady ? "All required Bridge routes and permissions passed." : report.Summary,
                true, "Required for Visual CSS execution, verification, managed history, and rollback.");
            AddPlugin("Yoast SEO", report.YoastDetected, report.YoastDetected, "Detected by WordPress", 
                report.YoastDetected ? "Yoast integration can be enabled when an adapter is available." : "Optional; not installed or inactive.",
                false, "Optional SEO provider.");
            AddPlugin("Rank Math", report.RankMathDetected, report.RankMathDetected, "Detected by WordPress",
                report.RankMathDetected ? "Rank Math integration can be enabled when an adapter is available." : "Optional; not installed or inactive.",
                false, "Optional SEO provider.");
            AddPlugin("Elementor", report.ElementorDetected, report.ElementorDetected, "Detected by WordPress",
                report.ElementorDetected ? "Elementor pages require the page-builder execution adapter." : "Optional; not installed or inactive.",
                false, "Required only for Elementor visual writes.");
            AddPlugin("Divi", report.DiviDetected, report.DiviDetected, "Detected by WordPress",
                report.DiviDetected ? "Divi pages require the page-builder execution adapter." : "Optional; not installed or inactive.",
                false, "Required only for Divi visual writes.");

            foreach (var check in report.Checks)
                Checks.Add(new BridgeDiagnosticRow(check.Name, check.Succeeded, check.Status, check.Details, check.DurationMilliseconds));

            DetectedPlugins = Plugins.Count(x => x.Detected);
            CompatiblePlugins = Plugins.Count(x => x.Detected && x.Compatible);
            BlockingIssues = report.Checks.Count(x => !x.Succeeded);
            OverallState = report.IsReady ? "READY" : "REVIEW";
            Status = report.IsReady
                ? $"READY • Bridge {BridgeVersion} passed all required checks. Optional plugins are shown separately."
                : $"REVIEW • {BlockingIssues} required diagnostic check(s) failed. Open the failed rows for exact remediation.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddPlugin(string name, bool detected, bool compatible, string version, string details, bool required, string purpose)
        => Plugins.Add(new PluginCompatibilityRow(name, detected, compatible, Empty(version), required, purpose, details));

    private void ResetSummary()
    {
        OverallState = "NOT TESTED";
        BridgeVersion = WordPressVersion = PhpVersion = Theme = "Unknown";
        DetectedPlugins = CompatiblePlugins = BlockingIssues = 0;
        Plugins.Clear();
        Checks.Clear();
    }

    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}

public sealed record PluginCompatibilityRow(
    string Name,
    bool Detected,
    bool Compatible,
    string Version,
    bool Required,
    string Purpose,
    string Details)
{
    public string DetectedText => Detected ? "Installed / active" : "Not detected";
    public string CompatibilityText => Compatible ? "Compatible" : Required ? "Blocked" : "Optional";
    public string RequiredText => Required ? "Required" : "Optional";
}
