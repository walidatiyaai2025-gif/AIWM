using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Desktop.Services.Sites;
using AIWordPressManager.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IApplicationPathService _paths;
    private readonly ICurrentSiteContext _currentSite;
    private readonly IDialogService _dialogs;

    public ObservableCollection<ReportMetricRow> Metrics { get; } = [];
    public ObservableCollection<ReportActivityRow> RecentActivity { get; } = [];
    public ObservableCollection<ReportChangeRow> ExecutedChanges { get; } = [];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ExportHtmlCommand { get; }
    public IAsyncRelayCommand ExportPdfCommand { get; }
    public IRelayCommand OpenExportsCommand { get; }
    public IRelayCommand OpenLatestReportCommand { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private int _scoreBefore;
    [ObservableProperty] private int _scoreAfter;
    [ObservableProperty] private int _scoreGain;
    [ObservableProperty] private string _statusMessage = "Reports summarize the current offline SQLite snapshot.";
    [ObservableProperty] private string _lastGeneratedText = "Not generated";
    [ObservableProperty] private string _latestReportPath = string.Empty;
    [ObservableProperty] private string _latestOptimizationRun = "No completed optimization run found";
    [ObservableProperty] private string _reportReadiness = "Select a website to generate its executive report.";

    public ReportsViewModel(
        IServiceScopeFactory scopeFactory,
        IApplicationPathService paths,
        ICurrentSiteContext currentSite,
        IDialogService dialogs)
    {
        _scopeFactory = scopeFactory;
        _paths = paths;
        _currentSite = currentSite;
        _dialogs = dialogs;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ExportHtmlCommand = new AsyncRelayCommand(ExportHtmlAsync, CanExport);
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, CanExport);
        OpenExportsCommand = new RelayCommand(OpenExports);
        OpenLatestReportCommand = new RelayCommand(OpenLatestReport, () => File.Exists(LatestReportPath));

        _currentSite.CurrentSiteChanged += (_, _) => _ = LoadAsync();
    }

    partial void OnIsBusyChanged(bool value)
    {
        LoadCommand.NotifyCanExecuteChanged();
        ExportHtmlCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    partial void OnLatestReportPathChanged(string value)
        => OpenLatestReportCommand.NotifyCanExecuteChanged();

    private bool CanExport() => !IsBusy && _currentSite.HasSite;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Metrics.Clear();
            RecentActivity.Clear();
            ExecutedChanges.Clear();
            ScoreBefore = 0;
            ScoreAfter = 0;
            ScoreGain = 0;

            if (!_currentSite.SiteId.HasValue)
            {
                HealthScore = 0;
                StatusMessage = "Select a site to build its executive SEO report.";
                ReportReadiness = "NOT READY • No website selected.";
                return;
            }

            var siteId = _currentSite.SiteId.Value;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var content = await db.WordPressContentRecords.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.IsAvailable);
            var seoIssues = await db.SeoAuditIssues.AsNoTracking().CountAsync(x => x.SiteId == siteId);
            var contentIssues = await db.ContentAuditIssues.AsNoTracking().CountAsync(x => x.SiteId == siteId);
            var broken = await db.BrokenLinks.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.Status != "Healthy");
            var suggestions = await db.SuggestedChanges.AsNoTracking().CountAsync(x => x.SiteId == siteId);
            var pending = await db.SuggestedChanges.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.ApprovalStatus == "Pending");
            var executed = await db.SuggestedChanges.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.ExecutionStatus == "Executed");
            var failed = await db.SuggestedChanges.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.ExecutionStatus == "Failed");
            var failedJobs = await db.ExecutionJobs.AsNoTracking().CountAsync(x => x.SiteId == siteId && x.Status == "Failed");

            var issueTotal = seoIssues + contentIssues + broken;
            HealthScore = content == 0
                ? 0
                : Math.Clamp(100 - (int)Math.Round(issueTotal * 100d / Math.Max(content * 4, 1)), 0, 100);

            LoadLatestOptimizationReceipt();
            if (ScoreAfter == 0) ScoreAfter = HealthScore;
            if (ScoreBefore == 0) ScoreBefore = HealthScore;
            ScoreGain = ScoreAfter - ScoreBefore;

            Metrics.Add(new("Content items", content, "Synchronized posts and pages", "DOC"));
            Metrics.Add(new("SEO findings", seoIssues, "Current measurable SEO findings", "SEO"));
            Metrics.Add(new("Content findings", contentIssues, "Structure and quality findings", "TXT"));
            Metrics.Add(new("Broken links", broken, "Unhealthy destinations", "URL"));
            Metrics.Add(new("AI actions", suggestions, $"{pending} awaiting approval", "AI"));
            Metrics.Add(new("Executed", executed, "Verified completed changes", "RUN"));
            Metrics.Add(new("Failed", failed + failedJobs, "Changes or jobs needing attention", "ERR"));
            Metrics.Add(new("Score gain", ScoreGain, $"{ScoreBefore} → {ScoreAfter}", "UP"));

            var jobs = await db.ExecutionJobs.AsNoTracking()
                .Where(x => x.SiteId == siteId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(15)
                .ToListAsync();
            foreach (var job in jobs)
            {
                RecentActivity.Add(new(
                    job.JobType,
                    job.Status,
                    job.CurrentStep,
                    job.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            }

            var changes = await db.SuggestedChanges.AsNoTracking()
                .Where(x => x.SiteId == siteId && (x.ExecutionStatus == "Executed" || x.ExecutionStatus == "Failed"))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(25)
                .ToListAsync();
            foreach (var change in changes)
            {
                ExecutedChanges.Add(new(
                    change.ChangeType,
                    change.ApprovalStatus,
                    change.ExecutionStatus,
                    change.CurrentValue ?? string.Empty,
                    change.ProposedValue ?? string.Empty,
                    change.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            }

            StatusMessage = $"Executive report ready for {_currentSite.SiteName}. Current score: {HealthScore}/100; open findings: {issueTotal}.";
            ReportReadiness = content == 0
                ? "REVIEW • Synchronize the website before relying on this report."
                : failed + failedJobs > 0
                    ? "REVIEW • The report contains failed operations that require attention."
                    : "READY • The executive report can be exported and shared.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Report loading failed: {ex.Message}";
            ReportReadiness = "BLOCKED • Resolve the report data error before export.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportHtmlAsync()
    {
        await LoadAsync();
        if (!_currentSite.HasSite) return;

        var file = BuildExportPath("html");
        await File.WriteAllTextAsync(file, BuildHtmlReport(), Encoding.UTF8);
        SetLatestReport(file);
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        await _dialogs.ShowInformationAsync("Executive report exported", $"The printable HTML report was created successfully.\n\n{file}");
    }

    private async Task ExportPdfAsync()
    {
        await LoadAsync();
        if (!_currentSite.HasSite) return;

        var htmlPath = BuildExportPath("html");
        var pdfPath = Path.ChangeExtension(htmlPath, ".pdf");
        await File.WriteAllTextAsync(htmlPath, BuildHtmlReport(), Encoding.UTF8);

        var edge = FindEdgeExecutable();
        if (edge is null)
        {
            SetLatestReport(htmlPath);
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            await _dialogs.ShowInformationAsync(
                "PDF export requires Microsoft Edge",
                "The printable report was opened as HTML. Use Print > Save as PDF. Microsoft Edge was not found in its standard installation paths.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = edge,
            Arguments = $"--headless --disable-gpu --no-pdf-header-footer --print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            await _dialogs.ShowErrorAsync("PDF export failed", "Microsoft Edge could not be started.");
            return;
        }

        await process.WaitForExitAsync();
        for (var attempt = 0; attempt < 20 && !File.Exists(pdfPath); attempt++)
            await Task.Delay(250);

        if (!File.Exists(pdfPath))
        {
            SetLatestReport(htmlPath);
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            await _dialogs.ShowInformationAsync("PDF export incomplete", "The HTML report was created, but Edge did not create the PDF. Use Print > Save as PDF from the opened report.");
            return;
        }

        SetLatestReport(pdfPath);
        Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
        await _dialogs.ShowInformationAsync("Executive PDF exported", $"The PDF report was created successfully.\n\n{pdfPath}");
    }

    private string BuildHtmlReport()
    {
        var generated = DateTime.Now;
        var statusClass = ScoreGain > 0 ? "good" : ScoreGain < 0 ? "bad" : "neutral";
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset='utf-8'>");
        html.Append("<meta name='viewport' content='width=device-width,initial-scale=1'>");
        html.Append($"<title>{WebUtility.HtmlEncode(_currentSite.SiteName)} — AI SEO Executive Report</title>");
        html.Append("<style>");
        html.Append("@page{size:A4;margin:16mm}*{box-sizing:border-box}body{font-family:'Segoe UI',Arial,sans-serif;background:#eef2f7;color:#172033;margin:0;padding:30px}main{max-width:1040px;margin:auto;background:#fff;border:1px solid #d7deea;border-radius:18px;overflow:hidden}header{padding:30px;background:#172033;color:#fff}h1{margin:0 0 8px;font-size:29px}h2{font-size:19px;margin:0 0 14px}.muted{color:#64748b}.section{padding:24px 30px;border-top:1px solid #e5eaf1}.hero{display:grid;grid-template-columns:1.2fr 1fr 1fr;gap:14px}.score,.stat{border:1px solid #d7deea;border-radius:14px;padding:18px;background:#f8fafc}.score strong{display:block;font-size:48px}.stat strong{display:block;font-size:28px}.good{color:#087f5b}.bad{color:#c92a2a}.neutral{color:#495057}.metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.metric{border:1px solid #d7deea;border-radius:12px;padding:14px}.metric b{display:block;font-size:25px;margin:5px 0}table{width:100%;border-collapse:collapse;font-size:12px}th,td{text-align:left;padding:10px;border-bottom:1px solid #e5eaf1;vertical-align:top}th{background:#edf2f7;color:#334155}.pill{display:inline-block;border-radius:999px;padding:5px 10px;background:#e8eef8;font-weight:600}.footer{padding:18px 30px;background:#f8fafc;color:#64748b;font-size:11px}@media(max-width:800px){body{padding:0}.hero,.metrics{grid-template-columns:1fr}main{border-radius:0}}@media print{body{background:#fff;padding:0}main{border:0;border-radius:0}.section{break-inside:avoid}}");
        html.Append("</style></head><body><main>");
        html.Append("<header>");
        html.Append($"<h1>{WebUtility.HtmlEncode(_currentSite.SiteName)} — AI SEO Executive Report</h1>");
        html.Append($"<div>Generated {generated:yyyy-MM-dd HH:mm:ss} • {WebUtility.HtmlEncode(_currentSite.SiteUrl)}</div>");
        html.Append("</header>");

        html.Append("<section class='section hero'>");
        html.Append($"<div class='score'><span class='muted'>CURRENT SEO HEALTH</span><strong>{HealthScore}/100</strong><span class='pill'>{WebUtility.HtmlEncode(ReportReadiness)}</span></div>");
        html.Append($"<div class='stat'><span class='muted'>OPTIMIZATION RUN</span><strong>{ScoreBefore} → {ScoreAfter}</strong><span>{WebUtility.HtmlEncode(LatestOptimizationRun)}</span></div>");
        html.Append($"<div class='stat'><span class='muted'>VERIFIED GAIN</span><strong class='{statusClass}'>{(ScoreGain >= 0 ? "+" : string.Empty)}{ScoreGain}</strong><span>points after latest recorded run</span></div>");
        html.Append("</section>");

        html.Append("<section class='section'><h2>Executive summary</h2>");
        html.Append($"<p>{WebUtility.HtmlEncode(StatusMessage)}</p>");
        html.Append("<p>This report combines the selected website's local WordPress snapshot, audit findings, AI action queue, execution history, and the latest optimization receipt. A WordPress write is considered successful only when the execution pipeline records verification.</p></section>");

        html.Append("<section class='section'><h2>Key metrics</h2><div class='metrics'>");
        foreach (var metric in Metrics)
            html.Append($"<div class='metric'><span class='muted'>{WebUtility.HtmlEncode(metric.Name)}</span><b>{metric.Value}</b><span>{WebUtility.HtmlEncode(metric.Details)}</span></div>");
        html.Append("</div></section>");

        html.Append("<section class='section'><h2>Recent execution activity</h2><table><thead><tr><th>Operation</th><th>Status</th><th>Current / final step</th><th>Updated</th></tr></thead><tbody>");
        foreach (var row in RecentActivity)
            html.Append($"<tr><td>{WebUtility.HtmlEncode(row.Operation)}</td><td>{WebUtility.HtmlEncode(row.Status)}</td><td>{WebUtility.HtmlEncode(row.Step)}</td><td>{WebUtility.HtmlEncode(row.Time)}</td></tr>");
        if (RecentActivity.Count == 0) html.Append("<tr><td colspan='4'>No execution activity is currently stored for this site.</td></tr>");
        html.Append("</tbody></table></section>");

        html.Append("<section class='section'><h2>Executed and failed AI changes</h2><table><thead><tr><th>Change</th><th>Approval</th><th>Execution</th><th>Before</th><th>After</th><th>Updated</th></tr></thead><tbody>");
        foreach (var row in ExecutedChanges)
            html.Append($"<tr><td>{WebUtility.HtmlEncode(row.ChangeType)}</td><td>{WebUtility.HtmlEncode(row.Approval)}</td><td>{WebUtility.HtmlEncode(row.Execution)}</td><td>{WebUtility.HtmlEncode(Trim(row.Before, 160))}</td><td>{WebUtility.HtmlEncode(Trim(row.After, 160))}</td><td>{WebUtility.HtmlEncode(row.Updated)}</td></tr>");
        if (ExecutedChanges.Count == 0) html.Append("<tr><td colspan='6'>No executed or failed AI changes are currently stored.</td></tr>");
        html.Append("</tbody></table></section>");

        html.Append("<section class='section'><h2>Safety and evidence contract</h2><ul><li>Low-risk supported actions only can enter automatic execution.</li><li>High-risk, staging-required, incomplete, and unsupported actions remain blocked.</li><li>Backups, WordPress API response logs, read-back verification, evidence capture, transaction history, and rollback availability are retained by the execution pipeline.</li></ul></section>");
        html.Append($"<div class='footer'>AI WordPress Manager • Report generated from local application data • {generated:O}</div>");
        html.Append("</main></body></html>");
        return html.ToString();
    }

    private void LoadLatestOptimizationReceipt()
    {
        var directory = Path.Combine(_paths.GetApplicationDataDirectory(), "OptimizationRuns");
        if (!Directory.Exists(directory)) return;

        var safeSite = Sanitize(_currentSite.SiteName);
        var receipt = Directory.EnumerateFiles(directory, $"optimization-{safeSite}-*.md", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (receipt is null) return;

        LatestOptimizationRun = receipt.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var line in File.ReadLines(receipt.FullName))
        {
            if (line.StartsWith("- SEO score before:", StringComparison.OrdinalIgnoreCase))
                ScoreBefore = ParseScore(line);
            else if (line.StartsWith("- SEO score after", StringComparison.OrdinalIgnoreCase))
                ScoreAfter = ParseScore(line);
        }
    }

    private string BuildExportPath(string extension)
    {
        var directory = Path.Combine(_paths.GetExportsDirectory(), "ExecutiveReports");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Sanitize(_currentSite.SiteName)}_AI_Executive_Report_{DateTime.Now:yyyyMMdd_HHmmss}.{extension}");
    }

    private void SetLatestReport(string path)
    {
        LatestReportPath = path;
        LastGeneratedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void OpenExports()
    {
        var directory = Path.Combine(_paths.GetExportsDirectory(), "ExecutiveReports");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OpenLatestReport()
    {
        if (File.Exists(LatestReportPath))
            Process.Start(new ProcessStartInfo(LatestReportPath) { UseShellExecute = true });
    }

    private static string? FindEdgeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static int ParseScore(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return 0;
        var token = line[(colon + 1)..].Trim().Split('/', StringSplitOptions.TrimEntries)[0];
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : 0;
    }

    private static string Trim(string value, int max)
        => string.IsNullOrWhiteSpace(value) || value.Length <= max ? value : value[..max] + "…";

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}

public sealed record ReportMetricRow(string Name, int Value, string Details, string Icon);
public sealed record ReportActivityRow(string Operation, string Status, string Step, string Time);
public sealed record ReportChangeRow(string ChangeType, string Approval, string Execution, string Before, string After, string Updated);
