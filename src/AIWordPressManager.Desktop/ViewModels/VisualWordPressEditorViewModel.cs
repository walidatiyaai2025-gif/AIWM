using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class VisualWordPressEditorViewModel : ObservableObject
{
    private readonly SitesViewModel _sites;
    private readonly IApplicationPathService _paths;
    private readonly IWordPressVisualCssService _visualCssService;

    public IRelayCommand OpenBeforeImageCommand { get; }
    public IRelayCommand OpenAfterImageCommand { get; }
    public IRelayCommand OpenEvidenceFolderCommand { get; }
    public IAsyncRelayCommand CheckBridgeCommand { get; }
    public IAsyncRelayCommand RunBridgeDiagnosticsCommand { get; }
    public IRelayCommand OpenBridgePluginFolderCommand { get; }
    public IRelayCommand OpenWordPressPluginUploadCommand { get; }
    public IAsyncRelayCommand RunSafeBridgeTestCommand { get; }
    public IAsyncRelayCommand ExecuteVisualCssCommand { get; }
    public IAsyncRelayCommand RollbackVisualCssCommand { get; }
    public IAsyncRelayCommand RefreshManagedHistoryCommand { get; }
    public IAsyncRelayCommand RollbackManagedHistoryCommand { get; }

    [ObservableProperty] private string _pageUrl = string.Empty;
    [ObservableProperty] private string _statusMessage = "Select a site, then load its public page.";
    [ObservableProperty] private bool _isBrowserReady;
    [ObservableProperty] private bool _isInspectionEnabled;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectedTag = "—";
    [ObservableProperty] private string _selectedSelector = string.Empty;
    [ObservableProperty] private string _selectedText = string.Empty;
    [ObservableProperty] private string _selectedId = string.Empty;
    [ObservableProperty] private string _selectedClasses = string.Empty;
    [ObservableProperty] private string _currentComputedStyle = string.Empty;
    [ObservableProperty] private string _previewCss = "font-size: 16px;\nline-height: 1.55;";
    [ObservableProperty] private string _beforeScreenshotPath = string.Empty;
    [ObservableProperty] private string _afterScreenshotPath = string.Empty;
    [ObservableProperty] private string _proposalStatus = "No visual execution proposal has been prepared.";
    [ObservableProperty] private string _lastConsoleMessage = string.Empty;
    [ObservableProperty] private string _bridgeStatus = "Bridge capability has not been checked.";
    [ObservableProperty] private bool _bridgeAvailable;
    [ObservableProperty] private bool _canExecuteVisualCss;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _lastChangeId = string.Empty;
    [ObservableProperty] private string _lastRollbackToken = string.Empty;
    [ObservableProperty] private string _executionResponse = string.Empty;
    [ObservableProperty] private string _verificationStatus = "Not verified";
    [ObservableProperty] private string _bridgeDiagnosticsSummary = "Run the full diagnostics to validate authentication, endpoints, permissions, version, and rollback support.";
    [ObservableProperty] private string _bridgeEnvironmentSummary = "Environment has not been inspected.";
    [ObservableProperty] private DateTimeOffset? _bridgeLastTestedAtUtc;
    [ObservableProperty] private bool _bridgeDiagnosticsReady;
    [ObservableProperty] private string _safeBridgeTestSummary = "Run the safe dry-run test before the first visual execution.";
    [ObservableProperty] private bool _safeBridgeTestPassed;
    [ObservableProperty] private DateTimeOffset? _safeBridgeTestedAtUtc;
    [ObservableProperty] private ManagedVisualCssHistoryRow? _selectedManagedHistoryItem;
    [ObservableProperty] private string _managedHistorySummary = "Load managed changes to review WordPress Visual CSS history.";
    [ObservableProperty] private string _managedHistoryChecksum = "—";
    [ObservableProperty] private int _managedRuleCount;

    public ObservableCollection<BridgeDiagnosticRow> BridgeDiagnosticChecks { get; } = new();
    public ObservableCollection<ManagedVisualCssHistoryRow> ManagedHistory { get; } = new();

    public event EventHandler? VisualCssApplied;

    public VisualWordPressEditorViewModel(
        SitesViewModel sites,
        IApplicationPathService paths,
        IWordPressVisualCssService visualCssService)
    {
        _sites = sites;
        _paths = paths;
        _visualCssService = visualCssService;

        OpenBeforeImageCommand = new RelayCommand(
            () => OpenPath(BeforeScreenshotPath),
            () => File.Exists(BeforeScreenshotPath));
        OpenAfterImageCommand = new RelayCommand(
            () => OpenPath(AfterScreenshotPath),
            () => File.Exists(AfterScreenshotPath));
        OpenEvidenceFolderCommand = new RelayCommand(() => OpenPath(GetEvidenceDirectory()));
        CheckBridgeCommand = new AsyncRelayCommand(CheckBridgeAsync, () => !IsExecuting && _sites.SelectedSite is not null);
        RunBridgeDiagnosticsCommand = new AsyncRelayCommand(RunBridgeDiagnosticsAsync, () => !IsExecuting && _sites.SelectedSite is not null);
        OpenBridgePluginFolderCommand = new RelayCommand(OpenBridgePluginFolder);
        OpenWordPressPluginUploadCommand = new RelayCommand(OpenWordPressPluginUploadPage, () => _sites.SelectedSite is not null);
        RunSafeBridgeTestCommand = new AsyncRelayCommand(RunSafeBridgeTestAsync, () => !IsExecuting && _sites.SelectedSite is not null && HasSelection && !string.IsNullOrWhiteSpace(PreviewCss));
        ExecuteVisualCssCommand = new AsyncRelayCommand(ExecuteVisualCssAsync, CanExecuteVisualCssNow);
        RollbackVisualCssCommand = new AsyncRelayCommand(RollbackVisualCssAsync, CanRollbackVisualCssNow);
        RefreshManagedHistoryCommand = new AsyncRelayCommand(RefreshManagedHistoryAsync, () => !IsExecuting && _sites.SelectedSite is not null);
        RollbackManagedHistoryCommand = new AsyncRelayCommand(RollbackManagedHistoryAsync, CanRollbackManagedHistory);

        _sites.SelectedSiteChanged += (_, _) => RefreshSiteContext();
        RefreshSiteContext();
    }

    partial void OnPreviewCssChanged(string value)
    {
        SafeBridgeTestPassed = false;
        SafeBridgeTestedAtUtc = null;
        SafeBridgeTestSummary = "CSS changed. Run the safe dry-run test again before execution.";
        RefreshCommandStates();
    }

    partial void OnSelectedManagedHistoryItemChanged(ManagedVisualCssHistoryRow? value)
    {
        RollbackManagedHistoryCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        RefreshSiteContext();
        if (_sites.SelectedSite is not null)
        {
            await CheckBridgeAsync();
            await RefreshManagedHistoryAsync();
        }
    }

    public string GetEvidenceDirectory()
    {
        var safeSite = SanitizeFileName(_sites.SelectedSite?.Name ?? "NoSite");
        var directory = Path.Combine(_paths.GetApplicationDataDirectory(), "Screenshots", "VisualEditor", safeSite);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public string GetProposalLogPath()
    {
        var directory = Path.Combine(_paths.GetLogsDirectory(), "visual-editor");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "visual-execution-proposals.jsonl");
    }

    public object BuildProposalRecord() => new
    {
        createdAtUtc = DateTimeOffset.UtcNow,
        siteId = _sites.SelectedSite?.Id,
        siteName = _sites.SelectedSite?.Name,
        pageUrl = PageUrl,
        selector = SelectedSelector,
        element = new
        {
            tag = SelectedTag,
            id = SelectedId,
            classes = SelectedClasses,
            text = SelectedText,
            currentComputedStyle = CurrentComputedStyle
        },
        proposedCss = PreviewCss,
        evidence = new
        {
            before = BeforeScreenshotPath,
            after = AfterScreenshotPath
        },
        route = "Visual CSS Executor",
        status = BridgeAvailable ? "ReadyForApprovedExecution" : "PreparedForReview",
        safety = new
        {
            writesToWordPress = BridgeAvailable,
            requiresBridge = true,
            requiresBackup = true,
            requiresVerification = true,
            rollbackAvailable = !string.IsNullOrWhiteSpace(LastRollbackToken)
        }
    };

    public async Task SaveProposalAsync(CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(BuildProposalRecord());
        await File.AppendAllTextAsync(GetProposalLogPath(), json + Environment.NewLine, cancellationToken);
        ProposalStatus = BridgeAvailable
            ? "Visual proposal saved locally. The bridge is ready for an approved execution."
            : "Visual proposal saved locally. Install or activate the bundled bridge before execution.";
    }

    public void SetSelection(ElementSelectionMessage selection)
    {
        SelectedTag = string.IsNullOrWhiteSpace(selection.Tag) ? "—" : selection.Tag.ToUpperInvariant();
        SelectedSelector = selection.Selector ?? string.Empty;
        SelectedText = selection.Text ?? string.Empty;
        SelectedId = selection.Id ?? string.Empty;
        SelectedClasses = selection.Classes ?? string.Empty;
        CurrentComputedStyle = selection.ComputedStyle ?? string.Empty;
        HasSelection = !string.IsNullOrWhiteSpace(SelectedSelector);
        SafeBridgeTestPassed = false;
        SafeBridgeTestedAtUtc = null;
        SafeBridgeTestSummary = "Selection changed. Run the safe dry-run test before execution.";
        StatusMessage = HasSelection
            ? $"Selected {SelectedTag} using {SelectedSelector}. Preview the CSS, capture evidence, then execute after review."
            : "No element was selected.";
        RefreshCommandStates();
    }

    public void SetBeforeScreenshot(string path)
    {
        BeforeScreenshotPath = path;
        OpenBeforeImageCommand.NotifyCanExecuteChanged();
        RefreshCommandStates();
    }

    public void SetAfterScreenshot(string path)
    {
        AfterScreenshotPath = path;
        OpenAfterImageCommand.NotifyCanExecuteChanged();
    }

    public void SetVerificationResult(bool verified, string details)
    {
        VerificationStatus = verified ? "Verified on the reloaded public page" : "Verification failed";
        StatusMessage = details;
    }

    private async Task RunBridgeDiagnosticsAsync()
    {
        if (_sites.SelectedSite is null)
        {
            BridgeDiagnosticsReady = false;
            BridgeDiagnosticsSummary = "Select a site before running bridge diagnostics.";
            return;
        }

        IsExecuting = true;
        BridgeDiagnosticChecks.Clear();
        BridgeDiagnosticsSummary = "Running authenticated bridge diagnostics…";
        BridgeEnvironmentSummary = "Inspecting WordPress, PHP, theme, permissions, SEO plugins, and page builders…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.RunDiagnosticsAsync(_sites.SelectedSite.Id);
            if (result.IsFailure)
            {
                BridgeDiagnosticsReady = false;
                BridgeDiagnosticsSummary = "Diagnostics failed: " + result.Error.Message;
                BridgeEnvironmentSummary = "Open Sites, re-save the Application Password, confirm HTTPS, then retry.";
                return;
            }

            var report = result.Value;
            BridgeDiagnosticsReady = report.IsReady;
            BridgeDiagnosticsSummary = report.Summary;
            BridgeLastTestedAtUtc = report.TestedAtUtc;
            BridgeEnvironmentSummary = string.Join(
                " • ",
                $"WordPress {Fallback(report.WordPressVersion)}",
                $"PHP {Fallback(report.PhpVersion)}",
                $"Bridge {Fallback(report.PluginVersion)}",
                $"Theme {Fallback(report.ActiveTheme)}",
                $"Stylesheet {Fallback(report.ActiveStylesheet)}",
                $"Yoast {(report.YoastDetected ? "Yes" : "No")}",
                $"Rank Math {(report.RankMathDetected ? "Yes" : "No")}",
                $"Elementor {(report.ElementorDetected ? "Yes" : "No")}",
                $"Divi {(report.DiviDetected ? "Yes" : "No")}");

            foreach (var check in report.Checks)
            {
                BridgeDiagnosticChecks.Add(new BridgeDiagnosticRow(
                    check.Name,
                    check.Succeeded,
                    check.Status,
                    check.Details,
                    check.DurationMilliseconds));
            }

            BridgeAvailable = report.Checks.Any(check => check.Name == "Visual CSS capability" && check.Succeeded);
            CanExecuteVisualCss = report.IsReady;
            BridgeStatus = report.IsReady
                ? $"READY • Bridge {report.PluginVersion} passed full validation."
                : report.Summary;
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }


    private async Task RunSafeBridgeTestAsync()
    {
        if (_sites.SelectedSite is null)
        {
            SafeBridgeTestPassed = false;
            SafeBridgeTestSummary = "Select a site before running the safe Bridge test.";
            return;
        }

        IsExecuting = true;
        SafeBridgeTestPassed = false;
        SafeBridgeTestSummary = "Validating the selector and CSS through WordPress without writing any changes…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.ValidateAsync(
                _sites.SelectedSite.Id,
                new VisualCssValidationRequest(PageUrl, SelectedSelector, PreviewCss));

            SafeBridgeTestedAtUtc = DateTimeOffset.UtcNow;
            if (result.IsFailure)
            {
                SafeBridgeTestSummary = "Safe test failed: " + result.Error.Message;
                ExecutionResponse = result.Error.Message;
                return;
            }

            SafeBridgeTestPassed = result.Value.IsValid;
            SafeBridgeTestSummary = result.Value.IsValid
                ? $"PASS • No write performed • Theme {result.Value.ActiveStylesheet} • Existing managed rules {result.Value.ManagedRuleCount} • {result.Value.DurationMilliseconds} ms"
                : "Bridge rejected the dry-run validation: " + result.Value.Message;
            ExecutionResponse = result.Value.ResponseBody;
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }

    private void OpenWordPressPluginUploadPage()
    {
        var siteUrl = _sites.SelectedHomeUrl;
        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            StatusMessage = "Select a site before opening WordPress plugin upload.";
            return;
        }

        var uri = siteUrl.TrimEnd('/') + "/wp-admin/plugin-install.php?tab=upload";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    private void OpenBridgePluginFolder()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WordPressPlugins"),
            Path.Combine(Directory.GetCurrentDirectory(), "WordPressPlugins"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WordPressPlugins"))
        };

        var folder = candidates.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            StatusMessage = "The bundled WordPressPlugins folder was not found in this build.";
            return;
        }

        OpenPath(folder);
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;

    private async Task CheckBridgeAsync()
    {
        if (_sites.SelectedSite is null)
        {
            BridgeAvailable = false;
            CanExecuteVisualCss = false;
            BridgeStatus = "Select a site before checking the WordPress bridge.";
            RefreshCommandStates();
            return;
        }

        IsExecuting = true;
        BridgeStatus = "Checking the AI WordPress Manager Bridge…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.CheckCapabilityAsync(_sites.SelectedSite.Id);
            if (result.IsFailure)
            {
                BridgeAvailable = false;
                CanExecuteVisualCss = false;
                BridgeStatus = "Bridge unavailable: " + result.Error.Message;
                return;
            }

            BridgeAvailable = result.Value.BridgeAvailable;
            CanExecuteVisualCss = result.Value.BridgeAvailable && result.Value.CanEditThemeOptions;
            BridgeStatus = CanExecuteVisualCss
                ? $"READY • Bridge {result.Value.PluginVersion} • Theme {result.Value.ActiveStylesheet}"
                : "Bridge detected, but the WordPress user lacks edit_theme_options permission.";
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }


    private async Task<bool> EnsureBridgeReadyForExecutionAsync()
    {
        var diagnosticsFresh = BridgeDiagnosticsReady && BridgeLastTestedAtUtc is not null &&
                               DateTimeOffset.UtcNow - BridgeLastTestedAtUtc.Value < TimeSpan.FromMinutes(15);
        if (!diagnosticsFresh)
            await RunBridgeDiagnosticsAsync();

        if (!BridgeDiagnosticsReady)
        {
            StatusMessage = "Execution blocked: the WordPress Bridge diagnostics are not ready.";
            return false;
        }

        var safeTestFresh = SafeBridgeTestPassed && SafeBridgeTestedAtUtc is not null &&
                            DateTimeOffset.UtcNow - SafeBridgeTestedAtUtc.Value < TimeSpan.FromMinutes(15);
        if (!safeTestFresh)
            await RunSafeBridgeTestAsync();

        if (!SafeBridgeTestPassed)
        {
            StatusMessage = "Execution blocked: the safe Bridge dry-run test did not pass.";
            return false;
        }

        return true;
    }

    private async Task RefreshManagedHistoryAsync()
    {
        if (_sites.SelectedSite is null)
        {
            ManagedHistory.Clear();
            ManagedHistorySummary = "Select a site before loading managed Visual CSS history.";
            return;
        }

        IsExecuting = true;
        ManagedHistorySummary = "Loading managed Visual CSS history from WordPress…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.GetHistoryAsync(_sites.SelectedSite.Id);
            if (result.IsFailure)
            {
                ManagedHistory.Clear();
                ManagedHistorySummary = "History load failed: " + result.Error.Message;
                return;
            }

            ManagedHistory.Clear();
            foreach (var item in result.Value.Items)
            {
                ManagedHistory.Add(new ManagedVisualCssHistoryRow(
                    item.ChangeId,
                    item.PageUrl,
                    item.Selector,
                    item.CssDeclarations,
                    item.Status,
                    item.ActiveStylesheet,
                    item.ExecutedAtUtc,
                    item.RolledBackAtUtc,
                    item.ExecutedBy));
            }

            ManagedRuleCount = result.Value.ManagedRuleCount;
            ManagedHistoryChecksum = string.IsNullOrWhiteSpace(result.Value.ManagedCssChecksum)
                ? "—"
                : result.Value.ManagedCssChecksum;
            ManagedHistorySummary = $"{ManagedHistory.Count} recorded change(s) • {ManagedRuleCount} active managed rule(s) • Bridge {result.Value.PluginVersion}.";
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }

    private async Task RollbackManagedHistoryAsync()
    {
        if (_sites.SelectedSite is null || SelectedManagedHistoryItem is null || !CanRollbackManagedHistory())
            return;

        IsExecuting = true;
        StatusMessage = $"Rolling back managed CSS change {SelectedManagedHistoryItem.ChangeId}…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.RollbackHistoryAsync(
                _sites.SelectedSite.Id,
                SelectedManagedHistoryItem.ChangeId);

            if (result.IsFailure)
            {
                ExecutionResponse = result.Error.Message;
                StatusMessage = "Managed history rollback failed: " + result.Error.Message;
                return;
            }

            ExecutionResponse = result.Value.ResponseBody;
            VerificationStatus = "History rollback accepted; public-page verification required";
            StatusMessage = "WordPress restored the selected Custom CSS revision. Reloading the public page for verification…";
            await RefreshManagedHistoryAsync();
            VisualCssApplied?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }

    private bool CanRollbackManagedHistory() =>
        !IsExecuting &&
        _sites.SelectedSite is not null &&
        SelectedManagedHistoryItem is { IsActive: true };

    private async Task ExecuteVisualCssAsync()
    {
        if (_sites.SelectedSite is null || !CanExecuteVisualCssNow()) return;
        if (!await EnsureBridgeReadyForExecutionAsync()) return;

        IsExecuting = true;
        VerificationStatus = "Waiting for page reload verification";
        StatusMessage = "Creating a WordPress safety revision and applying the approved visual CSS…";
        RefreshCommandStates();
        try
        {
            await SaveProposalAsync();
            var result = await _visualCssService.ApplyAsync(
                _sites.SelectedSite.Id,
                new VisualCssExecutionRequest(
                    PageUrl,
                    SelectedSelector,
                    PreviewCss,
                    CurrentComputedStyle,
                    BeforeScreenshotPath));

            if (result.IsFailure)
            {
                ExecutionResponse = result.Error.Message;
                StatusMessage = "WordPress rejected the Visual CSS execution: " + result.Error.Message;
                VerificationStatus = "Execution failed";
                return;
            }

            LastChangeId = result.Value.ChangeId;
            LastRollbackToken = result.Value.RollbackToken;
            ExecutionResponse = result.Value.ResponseBody;
            StatusMessage = "WordPress accepted the CSS change. Reloading the public page for computed-style verification…";
            await RefreshManagedHistoryAsync();
            VisualCssApplied?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }

    private async Task RollbackVisualCssAsync()
    {
        if (_sites.SelectedSite is null || !CanRollbackVisualCssNow()) return;

        IsExecuting = true;
        StatusMessage = "Rolling back the last Visual CSS change…";
        RefreshCommandStates();
        try
        {
            var result = await _visualCssService.RollbackAsync(
                _sites.SelectedSite.Id,
                new VisualCssRollbackRequest(LastChangeId, LastRollbackToken));

            if (result.IsFailure)
            {
                ExecutionResponse = result.Error.Message;
                StatusMessage = "Rollback failed: " + result.Error.Message;
                return;
            }

            ExecutionResponse = result.Value.ResponseBody;
            LastChangeId = string.Empty;
            LastRollbackToken = string.Empty;
            VerificationStatus = "Rollback accepted; reload the page to verify";
            StatusMessage = "WordPress restored the previous managed CSS. Reloading the public page…";
            await RefreshManagedHistoryAsync();
            VisualCssApplied?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsExecuting = false;
            RefreshCommandStates();
        }
    }

    private bool CanExecuteVisualCssNow() =>
        !IsExecuting &&
        _sites.SelectedSite is not null &&
        CanExecuteVisualCss &&
        HasSelection &&
        !string.IsNullOrWhiteSpace(PreviewCss) &&
        File.Exists(BeforeScreenshotPath);

    private bool CanRollbackVisualCssNow() =>
        !IsExecuting &&
        _sites.SelectedSite is not null &&
        !string.IsNullOrWhiteSpace(LastChangeId) &&
        !string.IsNullOrWhiteSpace(LastRollbackToken);

    private void RefreshSiteContext()
    {
        PageUrl = _sites.SelectedHomeUrl;
        BridgeAvailable = false;
        CanExecuteVisualCss = false;
        LastChangeId = string.Empty;
        LastRollbackToken = string.Empty;
        ManagedHistory.Clear();
        SelectedManagedHistoryItem = null;
        ManagedRuleCount = 0;
        ManagedHistoryChecksum = "—";
        ManagedHistorySummary = "Load managed changes to review WordPress Visual CSS history.";
        SafeBridgeTestPassed = false;
        SafeBridgeTestedAtUtc = null;
        SafeBridgeTestSummary = "Run the safe dry-run test before the first visual execution.";
        BridgeStatus = _sites.SelectedSite is null
            ? "Select a site before checking the WordPress bridge."
            : "Bridge capability has not been checked for this site.";
        StatusMessage = _sites.SelectedSite is null
            ? "Select a site before opening the visual editor."
            : $"Ready to load {_sites.SelectedSite.Name}. Preview changes locally before approved execution.";
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        CheckBridgeCommand.NotifyCanExecuteChanged();
        RunBridgeDiagnosticsCommand.NotifyCanExecuteChanged();
        RunSafeBridgeTestCommand.NotifyCanExecuteChanged();
        OpenWordPressPluginUploadCommand.NotifyCanExecuteChanged();
        ExecuteVisualCssCommand.NotifyCanExecuteChanged();
        RollbackVisualCssCommand.NotifyCanExecuteChanged();
        RefreshManagedHistoryCommand.NotifyCanExecuteChanged();
        RollbackManagedHistoryCommand.NotifyCanExecuteChanged();
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var target = Directory.Exists(path) ? path : Path.GetFullPath(path);
        if (!Directory.Exists(target) && !File.Exists(target)) return;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "Site" : value;
    }
}

public sealed class ElementSelectionMessage
{
    public string? Type { get; set; }
    public string? Tag { get; set; }
    public string? Id { get; set; }
    public string? Classes { get; set; }
    public string? Text { get; set; }
    public string? Selector { get; set; }
    public string? ComputedStyle { get; set; }
}


public sealed record BridgeDiagnosticRow(
    string Name,
    bool Succeeded,
    string Status,
    string Details,
    long DurationMilliseconds)
{
    public string Result => Succeeded ? "PASS" : "FAIL";
    public string Duration => DurationMilliseconds <= 0 ? "—" : $"{DurationMilliseconds} ms";
}


public sealed record ManagedVisualCssHistoryRow(
    string ChangeId,
    string PageUrl,
    string Selector,
    string CssDeclarations,
    string Status,
    string ActiveStylesheet,
    DateTimeOffset ExecutedAtUtc,
    DateTimeOffset? RolledBackAtUtc,
    string ExecutedBy)
{
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
    public string DisplayStatus => IsActive ? "ACTIVE" : "ROLLED BACK";
    public string ExecutedAt => ExecutedAtUtc == DateTimeOffset.MinValue ? "—" : ExecutedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string RolledBackAt => RolledBackAtUtc is null ? "—" : RolledBackAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
