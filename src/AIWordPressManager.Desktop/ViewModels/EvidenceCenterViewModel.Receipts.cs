using System.IO;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class EvidenceCenterViewModel
{
    internal void MergeExecutionReceipts()
    {
        var receiptsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager",
            "Receipts");

        if (!Directory.Exists(receiptsDirectory))
        {
            RefreshFirstJourneyReadiness();
            return;
        }

        var existingPaths = Items
            .Select(item => item.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(receiptsDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => Path.GetExtension(path) is ".html" or ".json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(200))
        {
            if (existingPaths.Contains(path))
                continue;

            try
            {
                var info = new FileInfo(path);
                if (info.Length == 0)
                    continue;

                var baseItem = EvidenceItem.Create(path, "Execution Receipt", info.Length, info.LastWriteTimeUtc, receiptsDirectory);
                Items.Add(baseItem with { Category = "Receipt", Kind = baseItem.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? "JSON" : "HTML" });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var ordered = Items.OrderByDescending(item => item.ModifiedUtc).ToArray();
        Items.Clear();
        foreach (var item in ordered)
            Items.Add(item);

        RaiseSummary();
        RefreshFirstJourneyReadiness();
    }
}
