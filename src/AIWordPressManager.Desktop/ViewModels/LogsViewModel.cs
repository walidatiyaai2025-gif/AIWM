using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly IApplicationPathService _paths;
    public ObservableCollection<LogFileRow> Items { get; } = [];
    public ObservableCollection<WordPressApiLogRow> WordPressApiItems { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand OpenSelectedCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    public IRelayCommand ClearApiSearchCommand { get; }

    [ObservableProperty] private LogFileRow? _selectedItem;
    [ObservableProperty] private WordPressApiLogRow? _selectedApiItem;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _apiSearchText = string.Empty;
    [ObservableProperty] private string _selectedContent = "Select a log file to preview its latest entries.";
    [ObservableProperty] private string _selectedApiDetails = "Select a WordPress API request to inspect its request, response, timing, and AI interpretation.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Application logs are stored locally with rolling daily files.";

    public IEnumerable<LogFileRow> FilteredItems => string.IsNullOrWhiteSpace(SearchText)
        ? Items
        : Items.Where(x => x.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<WordPressApiLogRow> FilteredWordPressApiItems => string.IsNullOrWhiteSpace(ApiSearchText)
        ? WordPressApiItems
        : WordPressApiItems.Where(x => x.SearchText.Contains(ApiSearchText, StringComparison.OrdinalIgnoreCase));

    public int FileCount => Items.Count;
    public int ApiRequestCount => WordPressApiItems.Count;
    public int ApiSuccessCount => WordPressApiItems.Count(x => x.Success);
    public int ApiFailureCount => WordPressApiItems.Count(x => !x.Success);
    public string LatestLogText => Items.FirstOrDefault()?.LastWriteLocalText ?? "None";

    public LogsViewModel(IApplicationPathService paths)
    {
        _paths = paths;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedItem is not null);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ClearApiSearchCommand = new RelayCommand(() => ApiSearchText = string.Empty);
    }

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredItems));
    partial void OnApiSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredWordPressApiItems));

    partial void OnSelectedItemChanged(LogFileRow? value)
    {
        OpenSelectedCommand.NotifyCanExecuteChanged();
        SelectedContent = value is null ? "Select a log file to preview its latest entries." : ReadTail(value.FilePath);
    }

    partial void OnSelectedApiItemChanged(WordPressApiLogRow? value)
    {
        SelectedApiDetails = value is null
            ? "Select a WordPress API request to inspect its request, response, timing, and AI interpretation."
            : value.BuildDetails();
    }

    partial void OnIsBusyChanged(bool value) => LoadCommand.NotifyCanExecuteChanged();

    public Task LoadAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        IsBusy = true;
        try
        {
            var directory = _paths.GetLogsDirectory();
            Directory.CreateDirectory(directory);
            var selectedPath = SelectedItem?.FilePath;
            Items.Clear();
            foreach (var file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly)
                         .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc))
            {
                Items.Add(new LogFileRow(file.FullName, file.Length, file.LastWriteTimeUtc));
            }

            SelectedItem = selectedPath is null
                ? Items.FirstOrDefault()
                : Items.FirstOrDefault(x => x.FilePath == selectedPath) ?? Items.FirstOrDefault();

            LoadWordPressApiLog(Path.Combine(directory, "wordpress-api.log"));
            StatusMessage = Items.Count == 0
                ? "No log files exist yet."
                : $"Loaded {Items.Count} rolling log file(s) and {WordPressApiItems.Count} WordPress API response(s).";

            RaiseSummaryProperties();
        }
        finally
        {
            IsBusy = false;
        }
        return Task.CompletedTask;
    }

    private void LoadWordPressApiLog(string path)
    {
        var selectedCorrelationId = SelectedApiItem?.CorrelationId;
        WordPressApiItems.Clear();
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path).TakeLast(1000).Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var row = JsonSerializer.Deserialize<WordPressApiLogRow>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (row is not null) WordPressApiItems.Add(row);
                }
                catch (JsonException)
                {
                    // Ignore partial or legacy lines while preserving the rest of the log.
                }
            }
        }

        SelectedApiItem = selectedCorrelationId is null
            ? WordPressApiItems.FirstOrDefault()
            : WordPressApiItems.FirstOrDefault(x => x.CorrelationId == selectedCorrelationId) ?? WordPressApiItems.FirstOrDefault();
    }

    private void RaiseSummaryProperties()
    {
        OnPropertyChanged(nameof(FilteredItems));
        OnPropertyChanged(nameof(FilteredWordPressApiItems));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(ApiRequestCount));
        OnPropertyChanged(nameof(ApiSuccessCount));
        OnPropertyChanged(nameof(ApiFailureCount));
        OnPropertyChanged(nameof(LatestLogText));
    }

    private void OpenFolder()
    {
        var directory = _paths.GetLogsDirectory();
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    private void OpenSelected()
    {
        if (SelectedItem is null || !File.Exists(SelectedItem.FilePath)) return;
        Process.Start(new ProcessStartInfo(SelectedItem.FilePath) { UseShellExecute = true });
    }

    private static string ReadTail(string path)
    {
        try
        {
            if (!File.Exists(path)) return "The selected log file no longer exists.";
            var lines = File.ReadLines(path).TakeLast(250);
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"Unable to read the log file: {ex.Message}";
        }
    }
}

public sealed record LogFileRow(string FilePath, long FileSizeBytes, DateTime LastWriteUtc)
{
    public string FileName => Path.GetFileName(FilePath);
    public string SizeText => FileSizeBytes < 1024 * 1024
        ? $"{FileSizeBytes / 1024d:0.0} KB"
        : $"{FileSizeBytes / 1024d / 1024d:0.0} MB";
    public string LastWriteLocalText => LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed record WordPressApiLogRow(
    DateTime TimestampUtc,
    string CorrelationId,
    Guid SiteId,
    string Operation,
    string Method,
    string Endpoint,
    string? RequestBody,
    int HttpStatus,
    string ReasonPhrase,
    bool Success,
    long DurationMs,
    string? ResponseBody,
    string AiInterpretation)
{
    public string TimestampLocalText => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string StatusText => $"{HttpStatus} {ReasonPhrase}".Trim();
    public string ResultText => Success ? "Success" : "Failed";
    public string DurationText => $"{DurationMs:N0} ms";
    public string SearchText => string.Join(' ', Operation, Method, Endpoint, StatusText, ResultText, AiInterpretation);

    public string BuildDetails() => string.Join(
        Environment.NewLine,
        $"Time: {TimestampLocalText}",
        $"Correlation ID: {CorrelationId}",
        $"Site ID: {SiteId}",
        $"Operation: {Operation}",
        $"Request: {Method} {Endpoint}",
        $"HTTP response: {StatusText}",
        $"Duration: {DurationText}",
        $"Result: {ResultText}",
        string.Empty,
        "AI interpretation",
        AiInterpretation,
        string.Empty,
        "Request body",
        string.IsNullOrWhiteSpace(RequestBody) ? "(none)" : RequestBody,
        string.Empty,
        "WordPress response body",
        string.IsNullOrWhiteSpace(ResponseBody) ? "(empty response)" : ResponseBody);
}
