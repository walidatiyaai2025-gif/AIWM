using System.IO.Compression;

namespace AIWordPressManager.Desktop;

internal static class SupportBundleService
{
    public static string CreateBundle()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager");
        var logsDirectory = Path.Combine(appData, "Logs");
        var receiptsDirectory = Path.Combine(appData, "Receipts");
        var bundlesDirectory = Path.Combine(appData, "SupportBundles");
        Directory.CreateDirectory(bundlesDirectory);

        var bundlePath = Path.Combine(
            bundlesDirectory,
            $"AIWordPressManager_Support_{DateTime.Now:yyyyMMdd_HHmmss}_{BuildIdentityDisplay.Commit}.zip");

        using var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create);
        AddTextEntry(archive, "build-identity.txt", BuildIdentityDisplay.DiagnosticText);

        AddFileIfExists(archive, Path.Combine(logsDirectory, "support-snapshot.txt"), "support-snapshot.txt");
        AddFileIfExists(archive, Path.Combine(logsDirectory, "startup-history.log"), "startup-history.log");

        foreach (var logPath in EnumerateLatestFiles(logsDirectory, "application-*.log", 3))
            AddFileIfExists(archive, logPath, Path.Combine("logs", Path.GetFileName(logPath)));

        foreach (var receiptPath in EnumerateLatestFiles(receiptsDirectory, "ExecutionReceipt_*.*", 4))
            AddFileIfExists(archive, receiptPath, Path.Combine("receipts", Path.GetFileName(receiptPath)));

        return bundlePath;
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

    private static void AddFileIfExists(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
            return;

        try
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var target = entry.Open();
            source.CopyTo(target);
        }
        catch
        {
            // A support bundle should remain usable even when one active log file cannot be copied.
        }
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
