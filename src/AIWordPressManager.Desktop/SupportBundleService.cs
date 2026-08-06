using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AIWordPressManager.Desktop;

internal static class SupportBundleService
{
    private const int MaximumRetainedBundles = 10;

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
        var entryHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
        {
            AddTextEntry(archive, "build-identity.txt", BuildIdentityDisplay.DiagnosticText, includedEntries, entryHashes);
            AddSanitizedFileIfExists(archive, Path.Combine(logsDirectory, "support-snapshot.txt"), "support-snapshot.txt", includedEntries, entryHashes);
            AddSanitizedFileIfExists(archive, Path.Combine(logsDirectory, "startup-history.log"), "startup-history.log", includedEntries, entryHashes);

            foreach (var logPath in EnumerateLatestFiles(logsDirectory, "application-*.log", 3))
                AddSanitizedFileIfExists(archive, logPath, Path.Combine("logs", Path.GetFileName(logPath)), includedEntries, entryHashes);

            foreach (var receiptPath in EnumerateLatestFiles(receiptsDirectory, "ExecutionReceipt_*.*", 4))
                AddSanitizedFileIfExists(archive, receiptPath, Path.Combine("receipts", Path.GetFileName(receiptPath)), includedEntries, entryHashes);

            var manifest = BuildManifest(includedEntries, entryHashes);
            AddTextEntry(archive, "manifest.txt", manifest, includedEntries: null, entryHashes: null);
        }

        DeleteExpiredBundles(bundlePath);
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

    private static void DeleteExpiredBundles(string currentBundlePath)
    {
        try
        {
            var expiredBundles = Directory
                .EnumerateFiles(BundlesDirectory, "AIWordPressManager_Support_*.zip")
                .Where(path => !string.Equals(path, currentBundlePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Skip(MaximumRetainedBundles - 1)
                .ToArray();

            foreach (var expiredBundle in expiredBundles)
            {
                try
                {
                    File.Delete(expiredBundle);
                }
                catch
                {
                    // Cleanup must never prevent creation of a new support bundle.
                }
            }
        }
        catch
        {
            // Retention cleanup is best-effort and must remain non-blocking.
        }
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
        ICollection<string> includedEntries,
        IDictionary<string, string> entryHashes)
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
            AddTextEntry(archive, entryName, sanitized, includedEntries, entryHashes);
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

    private static string BuildManifest(
        IEnumerable<string> includedEntries,
        IReadOnlyDictionary<string, string> entryHashes)
    {
        var lines = new List<string>
        {
            "AI WordPress Manager Support Bundle",
            $"Created UTC: {DateTimeOffset.UtcNow:O}",
            $"Version: {BuildIdentityDisplay.Version}",
            $"Branch: {BuildIdentityDisplay.Branch}",
            $"Commit: {BuildIdentityDisplay.FullCommit}",
            $"Retention: latest {MaximumRetainedBundles} support bundles",
            "Sensitive values are automatically replaced with <REDACTED>.",
            "Integrity: SHA-256 is recorded for every included data entry.",
            string.Empty,
            "Included entries and SHA-256:"
        };

        foreach (var entry in includedEntries.OrderBy(entry => entry))
        {
            var hash = entryHashes.TryGetValue(entry, out var value) ? value : "unavailable";
            lines.Add($"- {entry} | SHA-256: {hash}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddTextEntry(
        ZipArchive archive,
        string entryName,
        string content,
        ICollection<string>? includedEntries,
        IDictionary<string, string>? entryHashes)
    {
        var normalizedEntryName = entryName.Replace('\\', '/');
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        var entry = archive.CreateEntry(normalizedEntryName, CompressionLevel.Optimal);
        using (var target = entry.Open())
            target.Write(bytes, 0, bytes.Length);

        includedEntries?.Add(normalizedEntryName);
        if (entryHashes is not null)
            entryHashes[normalizedEntryName] = Convert.ToHexString(SHA256.HashData(bytes));
    }
}
