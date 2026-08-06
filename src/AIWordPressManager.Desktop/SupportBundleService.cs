using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace AIWordPressManager.Desktop;

internal static class SupportBundleService
{
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?im)(?<key>api[_ -]?key|access[_ -]?token|refresh[_ -]?token|bearer|authorization|password|passwd|pwd|client[_ -]?secret|application[_ -]?password)\s*[:=]\s*(?<value>[^\r\n,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerTokenPattern = new(
        @"(?i)Bearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JsonSecretPattern = new(
        "(?i)(\\\"(?:apiKey|accessToken|refreshToken|password|clientSecret|applicationPassword)\\\"\\s*:\\s*\\\")[^\\\"]*(\\\")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BundlesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "SupportBundles");

    public static string CreateBundle()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager");
        var logsDirectory = Path.Combine(appData, "Logs");
        var receiptsDirectory = Path.Combine(appData, "Receipts");
        Directory.CreateDirectory(BundlesDirectory);

        var bundlePath = Path.Combine(
            BundlesDirectory,
            $"AIWordPressManager_Support_{DateTime.Now:yyyyMMdd_HHmmss}_{BuildIdentityDisplay.Commit}.zip");

        var includedEntries = new List<string>();
        using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);

        AddTextEntry(archive, "build-identity.txt", BuildIdentityDisplay.DiagnosticText, includedEntries);
        AddSanitizedFileIfExists(archive, Path.Combine(logsDirectory, "support-snapshot.txt"), "support-snapshot.txt", includedEntries);
        AddSanitizedFileIfExists(archive, Path.Combine(logsDirectory, "startup-history.log"), "startup-history.log", includedEntries);

        foreach (var logPath in EnumerateLatestFiles(logsDirectory, "application-*.log", 3))
            AddSanitizedFileIfExists(archive, logPath, Path.Combine("logs", Path.GetFileName(logPath)), includedEntries);

        foreach (var receiptPath in EnumerateLatestFiles(receiptsDirectory, "ExecutionReceipt_*.*", 4))
            AddSanitizedFileIfExists(archive, receiptPath, Path.Combine("receipts", Path.GetFileName(receiptPath)), includedEntries);

        var manifest = BuildManifest(includedEntries);
        AddTextEntry(archive, "manifest.txt", manifest, includedEntries: null);

        return bundlePath;
    }

    public static string? FindLatestBundle()
    {
        if (!Directory.Exists(BundlesDirectory))
            return null;

        return Directory.EnumerateFiles(BundlesDirectory, "AIWordPressManager_Support_*.zip")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateLatestFiles(string directory, string pattern, int count)
    {
        if (!Directory.Exists(directory))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(directory, pattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(count)
            .ToArray();
    }

    private static void AddSanitizedFileIfExists(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        ICollection<string> includedEntries)
    {
        if (!File.Exists(sourcePath))
            return;

        try
        {
            using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sanitized = Sanitize(reader.ReadToEnd());
            AddTextEntry(archive, entryName, sanitized, includedEntries);
        }
        catch
        {
            // A support bundle should remain usable even when one active log file cannot be copied.
        }
    }

    private static string Sanitize(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        var sanitized = SensitiveAssignmentPattern.Replace(
            content,
            match => $"{match.Groups["key"].Value}=<REDACTED>");
        sanitized = BearerTokenPattern.Replace(sanitized, "Bearer <REDACTED>");
        sanitized = JsonSecretPattern.Replace(sanitized, "$1<REDACTED>$2");
        return sanitized;
    }

    private static string BuildManifest(IEnumerable<string> includedEntries)
    {
        var lines = new List<string>
        {
            "AI WordPress Manager Support Bundle",
            $"Created UTC: {DateTimeOffset.UtcNow:O}",
            $"Version: {BuildIdentityDisplay.Version}",
            $"Branch: {BuildIdentityDisplay.Branch}",
            $"Commit: {BuildIdentityDisplay.FullCommit}",
            "Sensitive values are automatically replaced with <REDACTED>.",
            string.Empty,
            "Included entries:"
        };

        lines.AddRange(includedEntries.OrderBy(entry => entry).Select(entry => $"- {entry}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddTextEntry(
        ZipArchive archive,
        string entryName,
        string content,
        ICollection<string>? includedEntries)
    {
        var normalizedEntryName = entryName.Replace('\\', '/');
        var entry = archive.CreateEntry(normalizedEntryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
        includedEntries?.Add(normalizedEntryName);
    }
}
