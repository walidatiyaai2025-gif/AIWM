using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class TransactionCenterViewModel : ObservableObject
{
    private readonly IApplicationPathService _paths;
    private readonly SitesViewModel _sites;
    private readonly IDialogService _dialogs;
    private readonly List<WordPressTransactionItem> _allItems = [];

    public ObservableCollection<WordPressTransactionItem> Items { get; } = [];
    public ObservableCollection<string> Filters { get; } = ["All states", "Committed", "Failed", "Started", "Interrupted"];

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    public IRelayCommand OpenExecutionCenterCommand { get; }
    public IRelayCommand OpenEvidenceCommand { get; }
    public IRelayCommand OpenApiLogsCommand { get; }
    public IRelayCommand OpenJournalFolderCommand { get; }
    public IRelayCommand CopySelectedJsonCommand { get; }
    public IAsyncRelayCommand ExportCsvCommand { get; }
    public IRelayCommand ClearFilterCommand { get; }

    public event Action<string>? NavigationRequested;

    [ObservableProperty] private WordPressTransactionItem? _selectedItem;
    [ObservableProperty] private string _selectedFilter = "All states";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _status = "Load the transaction journal to review committed, failed, and interrupted WordPress writes.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DateTime? _lastLoadedUtc;

    public int TotalCount => Items.Count;
    public int CommittedCount => Items.Count(x => x.EffectiveState == "Committed");
    public int FailedCount => Items.Count(x => x.EffectiveState == "Failed");
    public int InterruptedCount => Items.Count(x => x.EffectiveState == "Interrupted");
    public int StartedCount => Items.Count(x => x.EffectiveState == "Started");
    public string LastLoadedText => LastLoadedUtc is null ? "Not loaded" : LastLoadedUtc.Value.ToLocalTime().ToString("g");

    public TransactionCenterViewModel(IApplicationPathService paths, SitesViewModel sites, IDialogService dialogs)
    {
        _paths = paths;
        _sites = sites;
        _dialogs = dialogs;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ReconcileCommand = new AsyncRelayCommand(ReconcileSelectedAsync, () => !IsBusy && SelectedItem is not null);
        OpenExecutionCenterCommand = new RelayCommand(() => NavigationRequested?.Invoke("Execution Center"));
        OpenEvidenceCommand = new RelayCommand(() => NavigationRequested?.Invoke("Evidence Center"));
        OpenApiLogsCommand = new RelayCommand(() => NavigationRequested?.Invoke("Logs"));
        OpenJournalFolderCommand = new RelayCommand(OpenJournalFolder);
        CopySelectedJsonCommand = new RelayCommand(CopySelectedJson, () => SelectedItem is not null);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Items.Count > 0 && !IsBusy);
        ClearFilterCommand = new RelayCommand(() => { SearchText = string.Empty; SelectedFilter = "All states"; ApplyFilter(); });
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedItemChanged(WordPressTransactionItem? value)
    {
        ReconcileCommand.NotifyCanExecuteChanged();
        CopySelectedJsonCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        LoadCommand.NotifyCanExecuteChanged();
        try
        {
            var file = GetJournalPath();
            _allItems.Clear();
            if (!File.Exists(file))
            {
                ApplyFilter();
                Status = "No WordPress transactions have been recorded yet.";
                return;
            }

            var events = new List<TransactionJournalEvent>();
            var invalidLines = 0;
            foreach (var line in await File.ReadAllLinesAsync(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<TransactionJournalEvent>(line, JsonOptions);
                    if (entry is not null) events.Add(entry);
                }
                catch (JsonException)
                {
                    invalidLines++;
                }
            }

            var selectedSiteId = _sites.SelectedSite?.Id;
            var groups = events
                .Where(x => selectedSiteId is null || x.SiteId == selectedSiteId)
                .GroupBy(x => x.TransactionId)
                .OrderByDescending(x => x.Max(e => e.Utc));

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(x => x.Utc).ToList();
                var first = ordered.First();
                var last = ordered.Last();
                var effectiveState = ResolveEffectiveState(ordered);
                _allItems.Add(new WordPressTransactionItem(
                    group.Key,
                    last.SiteId,
                    last.Site ?? first.Site ?? "Unknown site",
                    last.ChangeId,
                    last.ChangeType ?? first.ChangeType ?? "Unknown",
                    last.Executor ?? first.Executor ?? "Unknown executor",
                    last.Decision ?? first.Decision ?? "Unknown",
                    effectiveState,
                    first.Utc,
                    last.Utc,
                    ordered.Count,
                    last.Details ?? string.Empty,
                    BuildTimeline(ordered),
                    JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true })));
            }

            ApplyFilter();
            LastLoadedUtc = DateTime.UtcNow;
            Status = invalidLines == 0
                ? $"Loaded {_allItems.Count:N0} transaction(s). Interrupted transactions are detected when a Started record has no terminal event after ten minutes."
                : $"Loaded {_allItems.Count:N0} transaction(s); skipped {invalidLines:N0} malformed journal line(s).";
        }
        catch (Exception ex)
        {
            Status = $"Transaction journal could not be loaded: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            LoadCommand.NotifyCanExecuteChanged();
            ReconcileCommand.NotifyCanExecuteChanged();
            ExportCsvCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<WordPressTransactionItem> query = _allItems;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(x => x.Site.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.ChangeType.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Executor.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Decision.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.EffectiveState.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.Details.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.TransactionId.ToString().Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedFilter != "All states")
            query = query.Where(x => x.EffectiveState.Equals(SelectedFilter, StringComparison.OrdinalIgnoreCase));

        Items.Clear();
        foreach (var item in query) Items.Add(item);
        NotifySummary();
    }

    private async Task ReconcileSelectedAsync()
    {
        if (SelectedItem is null) return;
        if (SelectedItem.EffectiveState is "Committed" or "Failed")
        {
            await _dialogs.ShowInformationAsync("Transaction recovery", "This transaction already has a terminal journal state. Open Evidence or API Logs to inspect the recorded result.");
            return;
        }

        var confirm = await _dialogs.ConfirmAsync(
            "Reconcile interrupted transaction",
            "This operation does not write to WordPress. It appends a local recovery review event and directs you to Execution Center, API Logs, and Evidence for verification before any retry. Continue?");
        if (!confirm) return;

        IsBusy = true;
        try
        {
            var entry = new
            {
                utc = DateTime.UtcNow,
                SelectedItem.TransactionId,
                SelectedItem.SiteId,
                site = SelectedItem.Site,
                SelectedItem.ChangeId,
                SelectedItem.ChangeType,
                SelectedItem.Executor,
                SelectedItem.Decision,
                state = "RecoveryReview",
                details = "Marked for manual reconciliation. Verify WordPress API logs and evidence before retrying from Execution Center."
            };
            Directory.CreateDirectory(Path.GetDirectoryName(GetJournalPath())!);
            await File.AppendAllTextAsync(GetJournalPath(), JsonSerializer.Serialize(entry) + Environment.NewLine);
            Status = "Recovery review recorded. Open API Logs and Evidence, then retry only from the verified Execution Center workflow.";
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        var folder = Path.Combine(_paths.GetApplicationDataDirectory(), "Transactions", "Exports");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"wordpress-transactions-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var csv = new StringBuilder();
        csv.AppendLine("TransactionId,Site,ChangeType,Executor,Decision,State,StartedUtc,UpdatedUtc,Events,Details");
        foreach (var item in Items)
        {
            csv.AppendLine(string.Join(',',
                Csv(item.TransactionId.ToString()), Csv(item.Site), Csv(item.ChangeType), Csv(item.Executor), Csv(item.Decision),
                Csv(item.EffectiveState), Csv(item.StartedUtc.ToString("O")), Csv(item.UpdatedUtc.ToString("O")),
                item.EventCount.ToString(), Csv(item.Details)));
        }
        await File.WriteAllTextAsync(file, csv.ToString(), new UTF8Encoding(true));
        Status = $"Exported {Items.Count:N0} transaction(s) to {file}";
        OpenPath(folder);
    }

    private void CopySelectedJson()
    {
        if (SelectedItem is null) return;
        try { System.Windows.Clipboard.SetText(SelectedItem.RawJson); Status = "Selected transaction JSON copied."; }
        catch (Exception ex) { Status = $"Copy failed: {ex.Message}"; }
    }

    private void OpenJournalFolder()
    {
        var folder = Path.GetDirectoryName(GetJournalPath())!;
        Directory.CreateDirectory(folder);
        OpenPath(folder);
    }

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private string GetJournalPath() => Path.Combine(_paths.GetApplicationDataDirectory(), "Transactions", "wordpress-transactions.jsonl");

    private static string ResolveEffectiveState(IReadOnlyList<TransactionJournalEvent> events)
    {
        var terminal = events.LastOrDefault(x => x.State is "Committed" or "Failed");
        if (terminal is not null) return terminal.State!;
        if (events.Any(x => x.State == "RecoveryReview")) return "Interrupted";
        var last = events.Max(x => x.Utc);
        return DateTime.UtcNow - last > TimeSpan.FromMinutes(10) ? "Interrupted" : "Started";
    }

    private static string BuildTimeline(IEnumerable<TransactionJournalEvent> events) => string.Join(
        Environment.NewLine,
        events.OrderBy(x => x.Utc).Select(x => $"{x.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {x.State ?? "Unknown"}  {x.Details}"));

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private void NotifySummary()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CommittedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(InterruptedCount));
        OnPropertyChanged(nameof(StartedCount));
        OnPropertyChanged(nameof(LastLoadedText));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

public sealed class TransactionJournalEvent
{
    public DateTime Utc { get; set; }
    public Guid TransactionId { get; set; }
    public Guid? SiteId { get; set; }
    public string? Site { get; set; }
    public Guid ChangeId { get; set; }
    public string? ChangeType { get; set; }
    public string? Executor { get; set; }
    public string? Decision { get; set; }
    public string? State { get; set; }
    public string? Details { get; set; }
}

public sealed record WordPressTransactionItem(
    Guid TransactionId,
    Guid? SiteId,
    string Site,
    Guid ChangeId,
    string ChangeType,
    string Executor,
    string Decision,
    string EffectiveState,
    DateTime StartedUtc,
    DateTime UpdatedUtc,
    int EventCount,
    string Details,
    string Timeline,
    string RawJson);
