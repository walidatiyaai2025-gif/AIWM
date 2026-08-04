using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class HealthCenterViewModel : ObservableObject
{
    private readonly ICurrentSiteContext _currentSite;
    private readonly IWordPressVisualCssService _visualCssService;
    private readonly IApplicationPathService _paths;
    private readonly Stopwatch _stopwatch = new();

    public ObservableCollection<HealthCheckRow> Checks { get; } = new();
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<string?> OpenCommand { get; }
    public event Action<string>? NavigationRequested;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _overallState = "NOT TESTED";
    [ObservableProperty] private string _summary = "Run the health assessment to validate the desktop application, local storage, WordPress connection, Bridge, and execution safety.";
    [ObservableProperty] private int _passedCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private double _applicationMemoryMb;
    [ObservableProperty] private string _selectedSite = "No site selected";
    [ObservableProperty] private DateTimeOffset? _lastCheckedUtc;

    public string LastCheckedText => LastCheckedUtc is null
        ? "Never checked"
        : LastCheckedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public HealthCenterViewModel(
        ICurrentSiteContext currentSite,
        IWordPressVisualCssService visualCssService,
        IApplicationPathService paths)
    {
        _currentSite = currentSite;
        _visualCssService = visualCssService;
        _paths = paths;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        OpenCommand = new RelayCommand<string?>(destination =>
        {
            if (!string.IsNullOrWhiteSpace(destination)) NavigationRequested?.Invoke(destination);
        });
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();
    partial void OnLastCheckedUtcChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastCheckedText));

    public Task LoadAsync()
    {
        SelectedSite = _currentSite.SiteName;
        if (Checks.Count == 0) return RefreshAsync();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Checks.Clear();
        _stopwatch.Restart();
        try
        {
            SelectedSite = _currentSite.SiteName;
            AddLocalStorageChecks();
            AddRuntimeChecks();
            AddOperationalDataChecks();
            await AddWordPressChecksAsync();
            RecalculateSummary();
            LastCheckedUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _stopwatch.Stop();
            IsBusy = false;
        }
    }

    private void AddLocalStorageChecks()
    {
        CheckWritableDirectory("Desktop", "Application data", _paths.GetApplicationDataDirectory(), true);
        CheckWritableDirectory("Desktop", "Logs directory", _paths.GetLogsDirectory(), true);
        CheckWritableDirectory("Recovery", "Backup directory", _paths.GetBackupsDirectory(), true);

        var appData = _paths.GetApplicationDataDirectory();
        var databaseFiles = Directory.Exists(appData)
            ? Directory.EnumerateFiles(appData, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray()
            : Array.Empty<string>();

        Add("Local data", "SQLite database", databaseFiles.Length > 0 ? "PASS" : "WARNING",
            databaseFiles.Length > 0 ? $"Detected {databaseFiles.Length} local database file(s)." : "No SQLite file was discovered under the application data directory.",
            databaseFiles.Length > 0 ? "No action required." : "Start the application once and confirm database initialization completes.",
            databaseFiles.Length > 0 ? "Info" : "Warning");
    }

    private void AddRuntimeChecks()
    {
        using var process = Process.GetCurrentProcess();
        ApplicationMemoryMb = process.WorkingSet64 / 1024d / 1024d;
        var memoryState = ApplicationMemoryMb >= 1500 ? "FAIL" : ApplicationMemoryMb >= 800 ? "WARNING" : "PASS";
        Add("Runtime", "Application memory", memoryState,
            $"Current working set: {ApplicationMemoryMb:N0} MB.",
            memoryState == "PASS" ? "Memory usage is within the normal operating range." : "Open Performance & Memory, pause heavy jobs, and run memory cleanup before continuing.",
            memoryState == "FAIL" ? "Critical" : memoryState == "WARNING" ? "Warning" : "Info");

        Add("Runtime", ".NET runtime", "PASS", $"Runtime: {Environment.Version}; 64-bit process: {Environment.Is64BitProcess}.", "No action required.", "Info");
        Add("Runtime", "Operating system", OperatingSystem.IsWindows() ? "PASS" : "FAIL",
            Environment.OSVersion.VersionString,
            OperatingSystem.IsWindows() ? "Supported Windows runtime detected." : "This desktop application requires Windows.",
            OperatingSystem.IsWindows() ? "Info" : "Critical");
    }

    private void AddOperationalDataChecks()
    {
        var appData = _paths.GetApplicationDataDirectory();
        CheckFolderPresence("Automation", "Scheduler data", Path.Combine(appData, "Scheduler"), false,
            "Scheduler data is created after the first saved schedule.");
        CheckFolderPresence("Execution", "Transaction journal", Path.Combine(appData, "Transactions"), false,
            "Transaction data is created after the first production execution.");
        CheckFolderPresence("Evidence", "Evidence storage", Path.Combine(appData, "Screenshots"), false,
            "Evidence folders are created after the first screenshot-enabled execution.");
    }

    private async Task AddWordPressChecksAsync()
    {
        if (!_currentSite.HasSite)
        {
            Add("WordPress", "Active site", "WARNING", "No active site is selected.", "Select a site before running authenticated WordPress and Bridge diagnostics.", "Warning");
            return;
        }

        Add("WordPress", "Active site", "PASS", $"{_currentSite.SiteName} is selected.", "No action required.", "Info");
        var started = Stopwatch.StartNew();
        try
        {
            var result = await _visualCssService.RunDiagnosticsAsync(_currentSite.SiteId!.Value);
            started.Stop();
            if (result.IsFailure)
            {
                Add("WordPress", "Authenticated diagnostics", "FAIL", result.Error.Message,
                    "Open Sites, verify the URL, username, Application Password, HTTPS certificate, and Bridge activation.", "Critical", started.ElapsedMilliseconds);
                return;
            }

            var report = result.Value;
            Add("Bridge", "Bridge readiness", report.IsReady ? "PASS" : "FAIL",
                $"Bridge {Text(report.PluginVersion)}; WordPress {Text(report.WordPressVersion)}; PHP {Text(report.PhpVersion)}. {report.Summary}",
                report.IsReady ? "All required execution routes and permissions passed." : "Open Plugin Compatibility and resolve every failed required diagnostic.",
                report.IsReady ? "Info" : "Critical", started.ElapsedMilliseconds);

            foreach (var check in report.Checks)
            {
                Add("Bridge", check.Name, check.Succeeded ? "PASS" : "FAIL", check.Details,
                    check.Succeeded ? "No action required." : "Review the HTTP status, permissions, plugin version, and endpoint configuration.",
                    check.Succeeded ? "Info" : "Critical", check.DurationMilliseconds);
            }
        }
        catch (Exception exception)
        {
            started.Stop();
            Add("WordPress", "Authenticated diagnostics", "FAIL", exception.Message,
                "Open API Logs for the full exception and test the site connection again.", "Critical", started.ElapsedMilliseconds);
        }
    }

    private void CheckWritableDirectory(string category, string name, string path, bool required)
    {
        try
        {
            Directory.CreateDirectory(path);
            var testFile = Path.Combine(path, $".aiwp-health-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "health-check");
            File.Delete(testFile);
            Add(category, name, "PASS", path, "Directory exists and is writable.", "Info");
        }
        catch (Exception exception)
        {
            Add(category, name, required ? "FAIL" : "WARNING", $"{path} — {exception.Message}",
                "Check Windows folder permissions, antivirus protection, disk availability, and controlled-folder access.",
                required ? "Critical" : "Warning");
        }
    }

    private void CheckFolderPresence(string category, string name, string path, bool required, string missingMessage)
    {
        var exists = Directory.Exists(path);
        Add(category, name, exists ? "PASS" : required ? "FAIL" : "WARNING",
            exists ? path : missingMessage,
            exists ? "Storage is available." : "This is expected until the related feature creates its first record.",
            exists ? "Info" : required ? "Critical" : "Warning");
    }

    private void Add(string category, string name, string status, string details, string recommendation, string severity, long durationMs = 0)
        => Checks.Add(new HealthCheckRow(category, name, status, severity, details, recommendation, durationMs));

    private void RecalculateSummary()
    {
        TotalCount = Checks.Count;
        PassedCount = Checks.Count(x => x.Status == "PASS");
        WarningCount = Checks.Count(x => x.Status == "WARNING");
        FailedCount = Checks.Count(x => x.Status == "FAIL");
        OverallState = FailedCount > 0 ? "BLOCKED" : WarningCount > 0 ? "REVIEW" : "HEALTHY";
        Summary = FailedCount > 0
            ? $"BLOCKED • {FailedCount} critical check(s) failed. Production execution should remain disabled until they are resolved."
            : WarningCount > 0
                ? $"REVIEW • Core checks passed with {WarningCount} advisory warning(s)."
                : $"HEALTHY • All {TotalCount} checks passed in {_stopwatch.ElapsedMilliseconds:N0} ms.";
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
}

public sealed record HealthCheckRow(
    string Category,
    string Name,
    string Status,
    string Severity,
    string Details,
    string Recommendation,
    long DurationMilliseconds);
