using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ExecutionCenterViewModel
{
    private readonly object _receiptSync = new();
    private string? _lastReceiptFingerprint;
    private IRelayCommand? _openLatestReceiptCommand;

    [ObservableProperty]
    private string? _latestReceiptPath;

    [ObservableProperty]
    private string _latestReceiptStatus = "No execution receipt has been created in this session.";

    public IRelayCommand OpenLatestReceiptCommand =>
        _openLatestReceiptCommand ??= new RelayCommand(OpenLatestReceipt, () => File.Exists(LatestReceiptPath));

    partial void OnLatestReceiptPathChanged(string? value)
        => _openLatestReceiptCommand?.NotifyCanExecuteChanged();

    partial void OnQueueStateChanged(string value)
    {
        if (!IsReceiptTerminalState(value))
            return;

        _ = WriteExecutionReceiptSafeAsync(value);
    }

    private async Task WriteExecutionReceiptSafeAsync(string terminalState)
    {
        try
        {
            var site = _sites.SelectedSite;
            var completedAtUtc = LastExecutionUtc ?? DateTime.UtcNow;
            var fingerprint = $"{CurrentJobId:N}|{terminalState}|{completedAtUtc:O}|{StatusMessage}";

            lock (_receiptSync)
            {
                if (string.Equals(_lastReceiptFingerprint, fingerprint, StringComparison.Ordinal))
                    return;

                _lastReceiptFingerprint = fingerprint;
            }

            var receipt = new ExecutionReceiptDocument(
                ReceiptId: Guid.NewGuid(),
                JobId: CurrentJobId,
                SiteId: site?.Id,
                SiteName: site?.Name ?? "No site selected",
                SiteUrl: site?.SiteUrl ?? string.Empty,
                State: terminalState,
                Summary: StatusMessage,
                CurrentStep: CurrentStep,
                ProgressPercent: ProgressPercent,
                BeforeEvidencePath: BeforeEvidencePath,
                AfterEvidencePath: AfterEvidencePath,
                CompletedAtUtc: completedAtUtc,
                ApplicationVersion: typeof(ExecutionCenterViewModel).Assembly.GetName().Version?.ToString() ?? "unknown");

            var receiptsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager",
                "Receipts");
            Directory.CreateDirectory(receiptsDirectory);

            var safeSiteName = SanitizeFileName(receipt.SiteName);
            var stem = $"ExecutionReceipt_{receipt.CompletedAtUtc:yyyyMMdd_HHmmss}_{safeSiteName}_{receipt.ReceiptId:N}";
            var jsonPath = Path.Combine(receiptsDirectory, stem + ".json");
            var htmlPath = Path.Combine(receiptsDirectory, stem + ".html");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(receipt, options));
            await File.WriteAllTextAsync(htmlPath, BuildReceiptHtml(receipt, jsonPath));

            LatestReceiptPath = htmlPath;
            LatestReceiptStatus = $"Execution receipt saved: {Path.GetFileName(htmlPath)}";
        }
        catch (Exception exception)
        {
            LatestReceiptStatus = $"Execution finished, but its receipt could not be written: {exception.Message}";
        }
    }

    private void OpenLatestReceipt()
    {
        if (string.IsNullOrWhiteSpace(LatestReceiptPath) || !File.Exists(LatestReceiptPath))
            return;

        Process.Start(new ProcessStartInfo(LatestReceiptPath) { UseShellExecute = true });
    }

    private static bool IsReceiptTerminalState(string state) =>
        state.Equals("Completed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("Completed with failures", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Site" : cleaned;
    }

    private static string BuildReceiptHtml(ExecutionReceiptDocument receipt, string jsonPath)
    {
        static string Encode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        static string PathOrNone(string? value) => string.IsNullOrWhiteSpace(value) ? "Not available" : value;

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>AI WordPress Manager Execution Receipt</title>
<style>
body { font-family: Segoe UI, Arial, sans-serif; margin: 40px; color: #17212b; background: #f4f7f9; }
.card { max-width: 980px; margin: auto; background: white; border-radius: 14px; padding: 32px; box-shadow: 0 8px 30px rgba(0,0,0,.08); }
h1 { margin-top: 0; }
.badge { display: inline-block; padding: 6px 12px; border-radius: 20px; background: #e7f4ef; font-weight: 700; }
table { width: 100%; border-collapse: collapse; margin-top: 24px; }
th, td { padding: 12px; border-bottom: 1px solid #dde5ea; text-align: left; vertical-align: top; }
th { width: 230px; color: #52616b; }
pre { white-space: pre-wrap; background: #f7f9fa; padding: 16px; border-radius: 8px; }
.small { color: #667781; font-size: 13px; margin-top: 24px; }
</style>
</head>
<body>
<div class="card">
<h1>Execution Receipt</h1>
<div class="badge">{{Encode(receipt.State)}}</div>
<table>
<tr><th>Receipt ID</th><td>{{receipt.ReceiptId}}</td></tr>
<tr><th>Job ID</th><td>{{Encode(receipt.JobId?.ToString() ?? "Not assigned")}}</td></tr>
<tr><th>Site</th><td>{{Encode(receipt.SiteName)}}</td></tr>
<tr><th>Site URL</th><td>{{Encode(receipt.SiteUrl)}}</td></tr>
<tr><th>Completed UTC</th><td>{{receipt.CompletedAtUtc:O}}</td></tr>
<tr><th>Progress</th><td>{{receipt.ProgressPercent}}%</td></tr>
<tr><th>Final step</th><td>{{Encode(receipt.CurrentStep)}}</td></tr>
<tr><th>Before evidence</th><td>{{Encode(PathOrNone(receipt.BeforeEvidencePath))}}</td></tr>
<tr><th>After evidence</th><td>{{Encode(PathOrNone(receipt.AfterEvidencePath))}}</td></tr>
<tr><th>Application version</th><td>{{Encode(receipt.ApplicationVersion)}}</td></tr>
</table>
<h2>Execution summary</h2>
<pre>{{Encode(receipt.Summary)}}</pre>
<div class="small">Machine-readable JSON: {{Encode(jsonPath)}}</div>
</div>
</body>
</html>
""";
    }
}

public sealed record ExecutionReceiptDocument(
    Guid ReceiptId,
    Guid? JobId,
    Guid? SiteId,
    string SiteName,
    string SiteUrl,
    string State,
    string Summary,
    string CurrentStep,
    int ProgressPercent,
    string? BeforeEvidencePath,
    string? AfterEvidencePath,
    DateTime CompletedAtUtc,
    string ApplicationVersion);
