using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using Microsoft.Playwright;

namespace AIWordPressManager.Automation.Visual;

public sealed class VisualInspectionService(IApplicationPathService paths)
{
    private static readonly VisualViewport[] Viewports =
    [
        new("Desktop", 1440, 900),
        new("Tablet", 768, 1024),
        new("Mobile", 390, 844)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };


    public async Task<PlaywrightBrowserInstallResult> InstallChromiumAsync(
        IProgress<VisualInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
        if (!File.Exists(scriptPath))
        {
            return new(false,
                $"The Playwright installer script was not found at {scriptPath}. Build the Desktop project, then try again.");
        }

        progress?.Report(new(10, "Preparing browser installer", "Locating PowerShell and Playwright installer"));

        var shells = new[] { "pwsh.exe", "pwsh", "powershell.exe", "powershell" };
        Exception? lastError = null;
        foreach (var shell in shells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(new(20, "Installing Chromium", $"Running Playwright with {shell}"));
                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" install chromium",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    progress?.Report(new(100, "Chromium installed", "Playwright browser dependency is ready"));
                    return new(true, string.IsNullOrWhiteSpace(output)
                        ? "Playwright Chromium was installed successfully."
                        : output.Trim());
                }

                lastError = new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? $"The installer exited with code {process.ExitCode}."
                        : error.Trim());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;
            }
        }

        return new(false,
            $"Unable to install Playwright Chromium automatically. {lastError?.Message ?? "No PowerShell executable was available."}");
    }

    public async Task<IReadOnlyList<VisualInspectionResult>> InspectAsync(
        string siteUrl,
        IProgress<VisualInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("A valid HTTP or HTTPS website URL is required.", nameof(siteUrl));

        var runAtUtc = DateTime.UtcNow;
        var output = Path.Combine(paths.GetScreenshotsDirectory(), "VisualInspector", GetSiteKey(siteUrl), runAtUtc.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        var results = new List<VisualInspectionResult>();
        progress?.Report(new(5, "Starting browser", "Preparing Playwright Chromium"));

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        for (var index = 0; index < Viewports.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var viewport = Viewports[index];
            var percent = 12 + (int)Math.Round(index * 80d / Viewports.Length);
            progress?.Report(new(percent, $"Inspecting {viewport.Name}", $"Opening {viewport.Width}×{viewport.Height}"));

            var consoleErrors = new List<string>();
            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height },
                IgnoreHTTPSErrors = true
            });
            var page = await context.NewPageAsync();
            page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add(message.Text); };
            page.PageError += (_, error) => consoleErrors.Add(error);

            var response = await page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60000
            });

            const string inspectionScript = """
() => JSON.stringify({
    title: document.title || '',
    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
    documentWidth: document.documentElement.scrollWidth,
    viewportWidth: window.innerWidth,
    missingAltImages: Array.from(document.images).filter(x => !x.hasAttribute('alt') || !x.alt.trim()).length,
    brokenImages: Array.from(document.images).filter(x => x.complete && x.naturalWidth === 0).length,
    smallTextElements: Array.from(document.querySelectorAll('body *')).filter(x => {
        const style = getComputedStyle(x);
        const rect = x.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 && parseFloat(style.fontSize) < 12 && x.children.length === 0;
    }).length,
    smallTouchTargets: Array.from(document.querySelectorAll('a,button,input,select,textarea,[role="button"]')).filter(x => {
        const rect = x.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 && (rect.width < 44 || rect.height < 44);
    }).length
})
""";
            var json = await page.EvaluateAsync<string>(inspectionScript);
            var metrics = JsonSerializer.Deserialize<VisualPageMetrics>(json, JsonOptions) ?? new();
            var screenshotPath = Path.Combine(output, $"{index + 1:00}-{viewport.Name.ToLowerInvariant()}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });

            results.Add(new VisualInspectionResult(
                viewport.Name, viewport.Width, viewport.Height, screenshotPath,
                response?.Status ?? 0, metrics.Title ?? string.Empty, metrics.HorizontalOverflow,
                metrics.DocumentWidth, metrics.ViewportWidth, metrics.MissingAltImages,
                metrics.BrokenImages, metrics.SmallTextElements, metrics.SmallTouchTargets,
                consoleErrors.Take(20).ToArray()));
        }

        await SaveRunAsync(siteUrl, new VisualInspectionRun(runAtUtc, siteUrl, results), cancellationToken);
        progress?.Report(new(100, "Visual inspection complete", $"Saved {results.Count} responsive screenshots and offline history"));
        return results;
    }

    public async Task<IReadOnlyList<VisualInspectionResult>> LoadLatestAsync(
        string siteUrl,
        CancellationToken cancellationToken = default)
    {
        var run = await LoadLatestRunAsync(siteUrl, cancellationToken);
        return run?.Results ?? [];
    }

    public async Task<VisualInspectionRun?> LoadLatestRunAsync(
        string siteUrl,
        CancellationToken cancellationToken = default)
    {
        var path = GetLatestResultPath(siteUrl);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<VisualInspectionRun>(stream, JsonOptions, cancellationToken);
    }



    public async Task<VisualInspectionComparison?> LoadLatestComparisonAsync(
        string siteUrl,
        CancellationToken cancellationToken = default)
    {
        var directory = GetHistoryDirectory(siteUrl);
        if (!Directory.Exists(directory)) return null;

        var files = Directory.EnumerateFiles(directory, "*.json")
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        if (files.Length < 2) return null;

        static async Task<VisualInspectionRun?> ReadRunAsync(
            string file,
            CancellationToken token)
        {
            try
            {
                await using var stream = File.OpenRead(file);
                return await JsonSerializer.DeserializeAsync<VisualInspectionRun>(stream, JsonOptions, token);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var latest = await ReadRunAsync(files[0], cancellationToken);
        var previous = await ReadRunAsync(files[1], cancellationToken);
        if (latest is null || previous is null) return null;

        var rows = latest.Results.Select(current =>
        {
            var before = previous.Results.FirstOrDefault(x =>
                string.Equals(x.ViewportName, current.ViewportName, StringComparison.OrdinalIgnoreCase));

            return new VisualViewportComparison(
                current.ViewportName,
                before?.IssueCount ?? 0,
                current.IssueCount,
                (before?.IssueCount ?? 0) - current.IssueCount,
                before?.ScreenshotPath ?? string.Empty,
                current.ScreenshotPath);
        }).ToArray();

        return new VisualInspectionComparison(
            previous.RunAtUtc,
            latest.RunAtUtc,
            previous.Results.Sum(x => x.IssueCount),
            latest.Results.Sum(x => x.IssueCount),
            rows);
    }

    public async Task<IReadOnlyList<VisualInspectionRunSummary>> LoadHistoryAsync(
        string siteUrl,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var directory = GetHistoryDirectory(siteUrl);
        if (!Directory.Exists(directory)) return [];
        var files = Directory.EnumerateFiles(directory, "*.json")
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take));
        var summaries = new List<VisualInspectionRunSummary>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(file);
                var run = await JsonSerializer.DeserializeAsync<VisualInspectionRun>(stream, JsonOptions, cancellationToken);
                if (run is not null) summaries.Add(VisualInspectionRunSummary.FromRun(run));
            }
            catch (JsonException)
            {
                // Ignore a damaged history entry; the latest valid run remains available.
            }
        }
        return summaries;
    }

    public async Task<string> ExportHtmlAsync(
        string siteUrl,
        IReadOnlyCollection<VisualInspectionResult> results,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(paths.GetExportsDirectory(), "VisualInspector");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"visual-inspection-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html");
        var rows = string.Join(Environment.NewLine, results.Select(x =>
            $"<tr><td>{WebUtility.HtmlEncode(x.ViewportName)}</td><td>{x.SizeLabel}</td><td>{x.HttpStatus}</td><td>{x.IssueCount}</td><td>{x.MissingAltImages}</td><td>{x.BrokenImages}</td><td>{x.SmallTextElements}</td><td>{x.SmallTouchTargets}</td><td>{x.ConsoleErrors.Count}</td></tr>"));

        var html = new StringBuilder()
            .AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Visual inspection report</title>")
            .AppendLine("<style>body{font-family:Segoe UI,Arial;margin:32px;color:#222;background:#f7f5ef} .card{background:#fff;border:1px solid #c8a83b;border-radius:12px;padding:18px;margin-bottom:18px} table{border-collapse:collapse;width:100%;background:#fff} th,td{border:1px solid #ccc;padding:9px;text-align:left} th{background:#111;color:#d4af37}</style></head>")
            .Append("<body><h1>Visual inspection report</h1><div class=\"card\"><strong>Site:</strong> ")
            .Append(WebUtility.HtmlEncode(siteUrl))
            .Append("<br><strong>Generated:</strong> ")
            .Append(DateTime.Now.ToString("g"))
            .AppendLine("</div>")
            .AppendLine("<table><thead><tr><th>Viewport</th><th>Size</th><th>HTTP</th><th>Signals</th><th>Missing ALT</th><th>Broken images</th><th>Small text</th><th>Small targets</th><th>Console errors</th></tr></thead><tbody>")
            .AppendLine(rows)
            .AppendLine("</tbody></table></body></html>")
            .ToString();
        await File.WriteAllTextAsync(file, html, cancellationToken);
        return file;
    }

    private async Task SaveRunAsync(string siteUrl, VisualInspectionRun run, CancellationToken cancellationToken)
    {
        var latestPath = GetLatestResultPath(siteUrl);
        var historyDirectory = GetHistoryDirectory(siteUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(latestPath)!);
        Directory.CreateDirectory(historyDirectory);

        await WriteJsonAsync(latestPath, run, cancellationToken);
        var historyPath = Path.Combine(historyDirectory, $"{run.RunAtUtc:yyyyMMdd-HHmmssfff}.json");
        await WriteJsonAsync(historyPath, run, cancellationToken);
    }

    private static async Task WriteJsonAsync(string path, VisualInspectionRun run, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, run, JsonOptions, cancellationToken);
    }

    private string GetLatestResultPath(string siteUrl) =>
        Path.Combine(paths.GetScreenshotsDirectory(), "VisualInspector", GetSiteKey(siteUrl), "latest.json");

    private string GetHistoryDirectory(string siteUrl) =>
        Path.Combine(paths.GetScreenshotsDirectory(), "VisualInspector", GetSiteKey(siteUrl), "History");

    private static string GetSiteKey(string siteUrl) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(siteUrl))).ToLowerInvariant()[..16];

    private sealed record VisualViewport(string Name, int Width, int Height);
    private sealed class VisualPageMetrics
    {
        public string? Title { get; set; }
        public bool HorizontalOverflow { get; set; }
        public int DocumentWidth { get; set; }
        public int ViewportWidth { get; set; }
        public int MissingAltImages { get; set; }
        public int BrokenImages { get; set; }
        public int SmallTextElements { get; set; }
        public int SmallTouchTargets { get; set; }
    }
}

public sealed record VisualInspectionProgress(int Percent, string Step, string Detail);

public sealed record VisualInspectionRun(DateTime RunAtUtc, string SiteUrl, IReadOnlyList<VisualInspectionResult> Results);

public sealed record VisualInspectionRunSummary(DateTime RunAtUtc, int Viewports, int TotalIssues, int MissingAltImages,
    int BrokenImages, int SmallTextElements, int SmallTouchTargets, int ConsoleErrors)
{
    public string RunAtDisplay => RunAtUtc.ToLocalTime().ToString("g");
    public string TrendSummary => $"{TotalIssues} signals · {MissingAltImages} ALT · {SmallTouchTargets} targets";

    public static VisualInspectionRunSummary FromRun(VisualInspectionRun run) => new(
        run.RunAtUtc,
        run.Results.Count,
        run.Results.Sum(x => x.IssueCount),
        run.Results.Sum(x => x.MissingAltImages),
        run.Results.Sum(x => x.BrokenImages),
        run.Results.Sum(x => x.SmallTextElements),
        run.Results.Sum(x => x.SmallTouchTargets),
        run.Results.Sum(x => x.ConsoleErrors.Count));
}

public sealed record VisualInspectionResult(
    string ViewportName,
    int Width,
    int Height,
    string ScreenshotPath,
    int HttpStatus,
    string PageTitle,
    bool HorizontalOverflow,
    int DocumentWidth,
    int ViewportWidth,
    int MissingAltImages,
    int BrokenImages,
    int SmallTextElements,
    int SmallTouchTargets,
    IReadOnlyList<string> ConsoleErrors)
{
    public int IssueCount => (HorizontalOverflow ? 1 : 0) + MissingAltImages + BrokenImages + SmallTextElements + SmallTouchTargets + ConsoleErrors.Count;
    public string SizeLabel => $"{Width} × {Height}";
    public string StatusLabel => HttpStatus is >= 200 and < 400 ? "Captured" : $"HTTP {HttpStatus}";
    public string Summary => $"{IssueCount} signals · {MissingAltImages} missing ALT · {ConsoleErrors.Count} console errors";
}



public sealed record VisualInspectionComparison(
    DateTime PreviousRunAtUtc,
    DateTime LatestRunAtUtc,
    int PreviousIssues,
    int LatestIssues,
    IReadOnlyList<VisualViewportComparison> Viewports)
{
    public int Improvement => PreviousIssues - LatestIssues;
    public string Summary => Improvement switch
    {
        > 0 => $"Improved by {Improvement} signals",
        < 0 => $"Regressed by {-Improvement} signals",
        _ => "No signal change"
    };
}

public sealed record VisualViewportComparison(
    string ViewportName,
    int PreviousIssues,
    int LatestIssues,
    int Improvement,
    string PreviousScreenshotPath,
    string LatestScreenshotPath)
{
    public string ChangeLabel => Improvement switch
    {
        > 0 => $"-{Improvement} improved",
        < 0 => $"+{-Improvement} worse",
        _ => "No change"
    };
}

public sealed record PlaywrightBrowserInstallResult(bool Success, string Message);
