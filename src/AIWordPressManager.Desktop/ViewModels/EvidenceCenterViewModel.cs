using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using AIWordPressManager.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class EvidenceCenterViewModel : ObservableObject
{
    private readonly IApplicationPathService _paths;

    public ObservableCollection<EvidenceItem> Items { get; } = [];
    public ObservableCollection<string> TypeFilters { get; } = ["All", "Before", "After", "Screenshot", "JSON", "HTML", "Log", "Other"];

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand OpenSelectedCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand ShowBeforeCommand { get; }
    public IRelayCommand ShowAfterCommand { get; }

    [ObservableProperty] private EvidenceItem? _selectedItem;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedType = "All";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Evidence is stored locally and linked to execution and verification workflows.";
    [ObservableProperty] private string _selectedDetails = "Select an evidence item to inspect its metadata and related before/after files.";
    [ObservableProperty] private string? _previewPath;
    [ObservableProperty] private string? _beforePreviewPath;
    [ObservableProperty] private string? _afterPreviewPath;

    public IEnumerable<EvidenceItem> FilteredItems => Items.Where(MatchesFilter);
    public int TotalCount => Items.Count;
    public int BeforeCount => Items.Count(x => x.Kind == "Before");
    public int AfterCount => Items.Count(x => x.Kind == "After");
    public int VerifiedPairCount => Items.Where(x => x.PairKey.Length > 0)
        .GroupBy(x => x.PairKey, StringComparer.OrdinalIgnoreCase)
        .Count(g => g.Any(x => x.Kind == "Before") && g.Any(x => x.Kind == "After"));
    public string LatestEvidenceText => Items.FirstOrDefault()?.ModifiedLocalText ?? "None";

    public EvidenceCenterViewModel(IApplicationPathService paths)
    {
        _paths = paths;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedItem is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ShowBeforeCommand = new RelayCommand(() => PreviewPath = BeforePreviewPath, () => !string.IsNullOrWhiteSpace(BeforePreviewPath));
        ShowAfterCommand = new RelayCommand(() => PreviewPath = AfterPreviewPath, () => !string.IsNullOrWhiteSpace(AfterPreviewPath));
    }

    partial void OnIsBusyChanged(bool value) => LoadCommand.NotifyCanExecuteChanged();
    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredItems));
    partial void OnSelectedTypeChanged(string value) => OnPropertyChanged(nameof(FilteredItems));

    partial void OnSelectedItemChanged(EvidenceItem? value)
    {
        OpenSelectedCommand.NotifyCanExecuteChanged();
        if (value is null)
        {
            SelectedDetails = "Select an evidence item to inspect its metadata and related before/after files.";
            PreviewPath = null;
            BeforePreviewPath = null;
            AfterPreviewPath = null;
            NotifyPairCommands();
            return;
        }

        SelectedDetails = value.BuildDetails();
        PreviewPath = value.IsImage ? value.FilePath : null;
        ResolvePair(value);
        NotifyPairCommands();
    }

    public Task LoadAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        IsBusy = true;
        try
        {
            var screenshots = _paths.GetScreenshotsDirectory();
            var logs = _paths.GetLogsDirectory();
            Directory.CreateDirectory(screenshots);
            Directory.CreateDirectory(logs);

            var selectedPath = SelectedItem?.FilePath;
            var rows = new List<EvidenceItem>();
            AddDirectory(rows, screenshots, "Screenshot");
            AddDirectory(rows, Path.Combine(logs, "visual-editor"), "Log");

            Items.Clear();
            foreach (var row in rows.OrderByDescending(x => x.ModifiedUtc).Take(3000)) Items.Add(row);

            SelectedItem = selectedPath is null
                ? Items.FirstOrDefault()
                : Items.FirstOrDefault(x => string.Equals(x.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? Items.FirstOrDefault();

            StatusMessage = Items.Count == 0
                ? "No execution evidence has been captured yet. Run a verified execution or a visual inspection first."
                : $"Loaded {Items.Count:N0} evidence file(s), including {VerifiedPairCount:N0} before/after pair(s).";
            RaiseSummary();
        }
        finally
        {
            IsBusy = false;
        }
        return Task.CompletedTask;
    }

    private void AddDirectory(List<EvidenceItem> rows, string directory, string source)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length == 0) continue;
                rows.Add(EvidenceItem.Create(path, source, info.Length, info.LastWriteTimeUtc, directory));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private bool MatchesFilter(EvidenceItem item)
    {
        if (!string.Equals(SelectedType, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Kind, SelectedType, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(item.Category, SelectedType, StringComparison.OrdinalIgnoreCase)) return false;

        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return item.SearchText.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void ResolvePair(EvidenceItem item)
    {
        BeforePreviewPath = null;
        AfterPreviewPath = null;
        if (string.IsNullOrWhiteSpace(item.PairKey)) return;

        var siblings = Items.Where(x => string.Equals(x.PairKey, item.PairKey, StringComparison.OrdinalIgnoreCase)).ToList();
        BeforePreviewPath = siblings.FirstOrDefault(x => x.Kind == "Before" && x.IsImage)?.FilePath;
        AfterPreviewPath = siblings.FirstOrDefault(x => x.Kind == "After" && x.IsImage)?.FilePath;
    }

    private void OpenFolder()
    {
        var directory = _paths.GetScreenshotsDirectory();
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OpenSelected()
    {
        if (SelectedItem is null || !File.Exists(SelectedItem.FilePath)) return;
        Process.Start(new ProcessStartInfo(SelectedItem.FilePath) { UseShellExecute = true });
    }

    private void NotifyPairCommands()
    {
        ShowBeforeCommand.NotifyCanExecuteChanged();
        ShowAfterCommand.NotifyCanExecuteChanged();
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(BeforeCount));
        OnPropertyChanged(nameof(AfterCount));
        OnPropertyChanged(nameof(VerifiedPairCount));
        OnPropertyChanged(nameof(LatestEvidenceText));
    }
}

public sealed record EvidenceItem(
    string FilePath,
    string RelativePath,
    string FileName,
    string Extension,
    string Kind,
    string Category,
    string Source,
    string PairKey,
    long SizeBytes,
    DateTime ModifiedUtc)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    public bool IsImage => ImageExtensions.Contains(Extension);
    public string SizeText => SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024d:0.0} KB" : $"{SizeBytes / 1024d / 1024d:0.0} MB";
    public string ModifiedLocalText => ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SearchText => string.Join(' ', FileName, RelativePath, Kind, Category, Source, PairKey);

    public static EvidenceItem Create(string path, string source, long size, DateTime modifiedUtc, string root)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        var lower = name.ToLowerInvariant();
        var kind = lower.Contains("before") ? "Before" : lower.Contains("after") ? "After" :
            ImageExtensions.Contains(extension) ? "Screenshot" : extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase) ? "JSON" :
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase) ? "HTML" :
            extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ? "Log" : "Other";
        var category = Directory.GetParent(path)?.Name ?? source;
        var pairKey = BuildPairKey(path);
        return new EvidenceItem(path, Path.GetRelativePath(root, path), name, extension, kind, category, source, pairKey, size, modifiedUtc);
    }

    private static string BuildPairKey(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalized = fileName
            .Replace("before", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("after", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('-', '_', ' ');
        return string.IsNullOrWhiteSpace(normalized) ? Directory.GetParent(path)?.FullName ?? string.Empty : Path.Combine(Directory.GetParent(path)?.FullName ?? string.Empty, normalized);
    }

    public string BuildDetails() => string.Join(
        Environment.NewLine,
        $"Type: {Kind}",
        $"Category: {Category}",
        $"Source: {Source}",
        $"Modified: {ModifiedLocalText}",
        $"Size: {SizeText}",
        $"Pair key: {(string.IsNullOrWhiteSpace(PairKey) ? "(none)" : PairKey)}",
        string.Empty,
        "Relative path",
        RelativePath,
        string.Empty,
        "Full path",
        FilePath);
}
