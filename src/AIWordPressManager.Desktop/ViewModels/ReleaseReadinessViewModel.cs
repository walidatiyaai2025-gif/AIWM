using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ReleaseReadinessViewModel : ObservableObject
{
    private readonly string _root;

    public ObservableCollection<ReleaseCheckItem> Checks { get; } = [];

    [ObservableProperty] private string _status = "Ready to validate the release workspace.";
    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private int _passed;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _failed;
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _readinessPercent;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private DateTime? _lastValidatedAt;

    public IAsyncRelayCommand ValidateCommand { get; }
    public IRelayCommand OpenRootCommand { get; }
    public IAsyncRelayCommand ExportReportCommand { get; }

    public ReleaseReadinessViewModel()
    {
        _root = FindProjectRoot();
        RootPath = _root;
        ValidateCommand = new AsyncRelayCommand(ValidateAsync, () => !IsRunning);
        OpenRootCommand = new RelayCommand(OpenRoot);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => Checks.Count > 0);
    }

    partial void OnIsRunningChanged(bool value) => ValidateCommand.NotifyCanExecuteChanged();

    public Task LoadAsync() => ValidateAsync();

    private async Task ValidateAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Status = "Running source, XAML, packaging, Bridge, and release checks...";
        Checks.Clear();

        try
        {
            await Task.Run(() =>
            {
                var results = new List<ReleaseCheckItem>();
                CheckProjectStructure(results);
                CheckXaml(results);
                CheckSourceSafety(results);
                CheckWordPressBridge(results);
                CheckDocumentation(results);
                CheckReleasePackaging(results);
                CheckBuildArtifacts(results);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var item in results) Checks.Add(item);
                });
            });

            Passed = Checks.Count(x => x.Status == "Pass");
            Warnings = Checks.Count(x => x.Status == "Warning");
            Failed = Checks.Count(x => x.Status == "Fail");
            Total = Checks.Count;
            ReadinessPercent = Total == 0 ? 0 : Math.Clamp((int)Math.Round((Passed + Warnings * 0.5) * 100d / Total), 0, 100);
            LastValidatedAt = DateTime.Now;
            Status = Failed == 0
                ? Warnings == 0 ? "READY • All release checks passed." : $"REVIEW • {Warnings} non-blocking warning(s) remain."
                : $"BLOCKED • {Failed} release-blocking check(s) failed.";
        }
        catch (Exception ex)
        {
            Status = $"Validation failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            ExportReportCommand.NotifyCanExecuteChanged();
        }
    }

    private void CheckProjectStructure(List<ReleaseCheckItem> results)
    {
        Add(results, "Solution", File.Exists(Path.Combine(_root, "AIWordPressManager.sln")), "Project structure", "AIWordPressManager.sln", "The solution file must exist at the project root.");
        var projects = Directory.Exists(Path.Combine(_root, "src"))
            ? Directory.GetFiles(Path.Combine(_root, "src"), "*.csproj", SearchOption.AllDirectories)
            : [];
        Add(results, "Source projects", projects.Length >= 8, "Project structure", $"{projects.Length} project(s) found", "Expected the complete multi-project source tree.");
        Add(results, "Desktop project", File.Exists(Path.Combine(_root, "src", "AIWordPressManager.Desktop", "AIWordPressManager.Desktop.csproj")), "Project structure", "Desktop project", "The WPF application project is required.");
        Add(results, "Setup project", File.Exists(Path.Combine(_root, "Setup", "AIWordPressManager.Setup.csproj")), "Packaging", "Windows setup project", "The Release pipeline requires the Setup EXE project.");
    }

    private void CheckXaml(List<ReleaseCheckItem> results)
    {
        var desktop = Path.Combine(_root, "src", "AIWordPressManager.Desktop");
        if (!Directory.Exists(desktop))
        {
            Fail(results, "XAML validation", "XAML", "Desktop source folder is missing.");
            return;
        }

        var xamlFiles = Directory.GetFiles(desktop, "*.xaml", SearchOption.AllDirectories);
        var invalid = new List<string>();
        foreach (var file in xamlFiles)
        {
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                while (reader.Read()) { }
            }
            catch (Exception ex)
            {
                invalid.Add($"{Path.GetRelativePath(_root, file)}: {ex.Message}");
            }
        }
        Add(results, "XAML is well formed", invalid.Count == 0, "XAML", invalid.Count == 0 ? $"{xamlFiles.Length} file(s) parsed" : string.Join(" | ", invalid.Take(3)), "Malformed XAML blocks application startup.");

        var allText = string.Join("\n", xamlFiles.Select(File.ReadAllText));
        var definitions = Regex.Matches(allText, "x:Key=\"([^\"]+)\"").Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var references = Regex.Matches(allText, "(?:StaticResource|DynamicResource)\\s+([^} ,]+)").Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var frameworkKeys = new HashSet<string>(StringComparer.Ordinal) { "BoolToVisibility", "SystemParameters.VerticalScrollBarWidthKey" };
        var missing = references.Where(x => !definitions.Contains(x) && !frameworkKeys.Contains(x) && !x.StartsWith("{x:", StringComparison.Ordinal)).OrderBy(x => x).ToList();
        Add(results, "XAML resources", missing.Count == 0, "XAML", missing.Count == 0 ? $"{references.Count} referenced resource key(s) resolved" : "Potential missing keys: " + string.Join(", ", missing.Take(10)), "Missing resources can stop the Splash screen at startup.", missing.Count == 0 ? "Pass" : "Warning");
    }

    private void CheckSourceSafety(List<ReleaseCheckItem> results)
    {
        var sourceFiles = Directory.Exists(Path.Combine(_root, "src"))
            ? Directory.GetFiles(Path.Combine(_root, "src"), "*.cs", SearchOption.AllDirectories)
            : [];
        var riskyApplicationRefs = sourceFiles.Where(f => File.ReadAllText(f).Contains("Application.Current", StringComparison.Ordinal) &&
            !File.ReadAllText(f).Contains("System.Windows.Application.Current", StringComparison.Ordinal) &&
            !File.ReadAllText(f).Contains("using Application = System.Windows.Application", StringComparison.Ordinal)).ToList();
        Add(results, "WPF Application namespace", riskyApplicationRefs.Count == 0, "C# safety", riskyApplicationRefs.Count == 0 ? "No ambiguous Application.Current references" : string.Join(", ", riskyApplicationRefs.Take(5).Select(f => Path.GetFileName(f))), "Prevents collision with AIWordPressManager.Application.", riskyApplicationRefs.Count == 0 ? "Pass" : "Warning");

        var brokenMultiline = sourceFiles.Where(f => Regex.IsMatch(File.ReadAllText(f), "\"[^\"\\r\\n]*$", RegexOptions.Multiline)).Take(20).ToList();
        Add(results, "String literal heuristic", brokenMultiline.Count == 0, "C# safety", brokenMultiline.Count == 0 ? "No obvious unterminated string patterns" : $"Review {brokenMultiline.Count} file(s)", "This is a preventive heuristic; the compiler remains authoritative.", brokenMultiline.Count == 0 ? "Pass" : "Warning");
    }

    private void CheckWordPressBridge(List<ReleaseCheckItem> results)
    {
        var plugins = Path.Combine(_root, "WordPressPlugins");
        var zips = Directory.Exists(plugins) ? Directory.GetFiles(plugins, "AIWordPressManager-Bridge-*.zip") : [];
        Add(results, "Bundled WordPress Bridge", zips.Length > 0, "WordPress Bridge", zips.Length == 0 ? "No Bridge package found" : Path.GetFileName(zips.OrderByDescending(File.GetLastWriteTimeUtc).First()), "The installer should include the supported Bridge plugin.");

        if (zips.Length > 0)
        {
            var latest = zips.OrderByDescending(File.GetLastWriteTimeUtc).First();
            try
            {
                using var archive = ZipFile.OpenRead(latest);
                var php = archive.Entries.FirstOrDefault(x => x.FullName.EndsWith(".php", StringComparison.OrdinalIgnoreCase));
                Add(results, "Bridge package integrity", php is not null && php.Length > 0, "WordPress Bridge", php is null ? "PHP entry missing" : $"{archive.Entries.Count} file(s); {php.FullName}", "The ZIP must contain the plugin PHP entry point.");
            }
            catch (Exception ex)
            {
                Fail(results, "Bridge package integrity", "WordPress Bridge", ex.Message);
            }
        }
    }

    private void CheckDocumentation(List<ReleaseCheckItem> results)
    {
        var documentation = Path.Combine(_root, "src", "AIWordPressManager.Desktop", "Documentation");
        var docs = Directory.Exists(documentation) ? Directory.GetFiles(documentation, "*.docx") : [];
        Add(results, "Bundled documentation", docs.Length >= 1, "Documentation", $"{docs.Length} Word document(s)", "User guide and system roadmap should ship with the application.", docs.Length >= 1 ? "Pass" : "Warning");

        var files = Path.Combine(_root, "Files");
        var notes = Directory.Exists(files) ? Directory.GetFiles(files, "PART*_RELEASE_NOTES.md") : [];
        Add(results, "Release notes", notes.Length > 0, "Documentation", $"{notes.Length} release-note file(s)", "Every release should contain traceable change notes.", notes.Length > 0 ? "Pass" : "Warning");
    }

    private void CheckReleasePackaging(List<ReleaseCheckItem> results)
    {
        var setupRelease = Path.Combine(_root, "Setup", "Release");
        var setupExe = Directory.Exists(setupRelease) ? Directory.GetFiles(setupRelease, "*.exe").FirstOrDefault() : null;
        Add(results, "Ready Setup EXE", setupExe is not null, "Packaging", setupExe is null ? "Build Release to create Setup\\Release\\AIWordPressManager.Setup.exe" : Path.GetFileName(setupExe), "A distributable release requires the generated installer.", setupExe is null ? "Warning" : "Pass");

        var validators = Directory.Exists(Path.Combine(_root, "Build")) ? Directory.GetFiles(Path.Combine(_root, "Build"), "Validate-*.ps1") : [];
        Add(results, "Build validation tools", validators.Length >= 3, "Packaging", $"{validators.Length} validator script(s)", "Release validation should cover XAML resources, Bridge packaging, and the build.", validators.Length >= 3 ? "Pass" : "Warning");
    }

    private void CheckBuildArtifacts(List<ReleaseCheckItem> results)
    {
        var stale = Directory.Exists(_root)
            ? Directory.GetDirectories(_root, "obj", SearchOption.AllDirectories).Concat(Directory.GetDirectories(_root, "bin", SearchOption.AllDirectories)).Count()
            : 0;
        Add(results, "Build artifact awareness", true, "Build hygiene", $"{stale} bin/obj folder(s) detected", "The validation build should clean stale artifacts before compiling.", stale > 0 ? "Warning" : "Pass");
    }

    private async Task ExportReportAsync()
    {
        var folder = Path.Combine(_root, "Files", "ReleaseReadiness");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"release-readiness-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        var sb = new StringBuilder();
        sb.AppendLine("# AI WordPress Manager — Release Readiness");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Readiness: {ReadinessPercent}%");
        sb.AppendLine($"Passed: {Passed} | Warnings: {Warnings} | Failed: {Failed}");
        sb.AppendLine();
        foreach (var item in Checks)
            sb.AppendLine($"- **{item.Status}** · {item.Category} · {item.Name}: {item.Details} — {item.Guidance}");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        Status = $"Report exported: {path}";
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void OpenRoot()
    {
        if (Directory.Exists(_root)) Process.Start(new ProcessStartInfo("explorer.exe", _root) { UseShellExecute = true });
    }

    private static string FindProjectRoot()
    {
        var candidates = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory };
        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(candidate);
            for (var i = 0; current is not null && i < 10; i++, current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "AIWordPressManager.sln"))) return current.FullName;
            }
        }
        return Environment.CurrentDirectory;
    }

    private static void Add(List<ReleaseCheckItem> results, string name, bool condition, string category, string details, string guidance, string? explicitStatus = null)
        => results.Add(new ReleaseCheckItem(name, explicitStatus ?? (condition ? "Pass" : "Fail"), category, details, guidance));

    private static void Fail(List<ReleaseCheckItem> results, string name, string category, string details)
        => results.Add(new ReleaseCheckItem(name, "Fail", category, details, "Resolve this item before producing a Release build."));
}

public sealed record ReleaseCheckItem(string Name, string Status, string Category, string Details, string Guidance);
