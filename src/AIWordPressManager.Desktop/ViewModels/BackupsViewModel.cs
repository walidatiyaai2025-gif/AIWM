using System.IO;
using AIWordPressManager.Persistence;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IApplicationPathService _paths;
    private readonly IDialogService _dialogs;

    public ObservableCollection<BackupRow> Items { get; } = [];
    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand CreateBackupCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand OpenSelectedCommand { get; }
    public IAsyncRelayCommand RestoreSelectedCommand { get; }
    public IAsyncRelayCommand RestoreFromFileCommand { get; }
    public IAsyncRelayCommand ImportBackupCommand { get; }
    public IRelayCommand ExportSelectedCommand { get; }

    [ObservableProperty] private BackupRow? _selectedItem;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _currentStep = "Reading verified backups from SQLite.";
    [ObservableProperty] private string _statusMessage = "Backups are created locally and integrity-checked before they are listed.";

    public int VerifiedCount => Items.Count(x => x.IsVerified);
    public long TotalBytes => Items.Sum(x => x.FileSizeBytes);
    public string TotalSizeText => FormatBytes(TotalBytes);
    public string LatestBackupText => Items.FirstOrDefault()?.CreatedAtLocalText ?? "Never";

    public BackupsViewModel(IServiceScopeFactory scopeFactory, IApplicationPathService paths, IDialogService dialogs)
    {
        _scopeFactory = scopeFactory;
        _paths = paths;
        _dialogs = dialogs;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedItem is not null);
        RestoreSelectedCommand = new AsyncRelayCommand(RestoreSelectedAsync, () => !IsBusy && SelectedItem is not null && SelectedItem.IsVerified);
        RestoreFromFileCommand = new AsyncRelayCommand(RestoreFromFileAsync, () => !IsBusy);
        ImportBackupCommand = new AsyncRelayCommand(ImportBackupAsync, () => !IsBusy);
        ExportSelectedCommand = new RelayCommand(ExportSelected, () => SelectedItem is not null && File.Exists(SelectedItem.FilePath));
    }

    partial void OnSelectedItemChanged(BackupRow? value)
    {
        OpenSelectedCommand.NotifyCanExecuteChanged();
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsBusyChanged(bool value)
    {
        LoadCommand.NotifyCanExecuteChanged();
        CreateBackupCommand.NotifyCanExecuteChanged();
        RestoreSelectedCommand.NotifyCanExecuteChanged();
        RestoreFromFileCommand.NotifyCanExecuteChanged();
        ImportBackupCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ProgressPercent = 20;
        CurrentStep = "Opening backup history";
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.Backups.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(250).ToListAsync();
            Items.Clear();
            foreach (var row in rows)
            {
                Items.Add(new BackupRow(row.Id, row.FilePath, row.FileSizeBytes, row.IsVerified, row.CreatedAtUtc));
            }
            ProgressPercent = 100;
            CurrentStep = "Backup history ready";
            StatusMessage = $"Loaded {Items.Count} backup(s). Verified: {VerifiedCount}. Total size: {TotalSizeText}.";
            RaiseSummary();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateBackupAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ProgressPercent = 10;
        CurrentStep = "Checkpointing SQLite";
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            ProgressPercent = 35;
            CurrentStep = "Copying database";
            var path = await service.CreateBackupAsync();
            ProgressPercent = 85;
            CurrentStep = "Verifying backup integrity";
            StatusMessage = $"Verified backup created: {path}";
            await LoadAsyncInternal();
            await _dialogs.ShowInformationAsync("Backup created", $"A verified SQLite backup was created successfully.\n\n{path}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            await _dialogs.ShowErrorAsync("Backup failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
            CurrentStep = "Ready";
        }
    }

    private async Task LoadAsyncInternal()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Backups.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(250).ToListAsync();
        Items.Clear();
        foreach (var row in rows) Items.Add(new BackupRow(row.Id, row.FilePath, row.FileSizeBytes, row.IsVerified, row.CreatedAtUtc));
        RaiseSummary();
    }


    private Task RestoreSelectedAsync()
    {
        if (SelectedItem is null) return Task.CompletedTask;
        return PrepareRestoreAsync(SelectedItem.FilePath);
    }

    private async Task RestoreFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select AI WordPress Manager database backup",
            Filter = "SQLite database (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;
        await PrepareRestoreAsync(dialog.FileName);
    }


    private async Task ImportBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import AI WordPress Manager database backup",
            Filter = "SQLite database (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;
        if (IsBusy) return;

        IsBusy = true;
        ProgressPercent = 15;
        CurrentStep = "Validating imported backup";
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            // Verify through the same restore preparation service without scheduling a restore.
            // A temporary plan is avoided; integrity is checked by opening a read-only SQLite connection here.
            var source = dialog.FileName;
            await VerifyImportedDatabaseAsync(source);

            var backupDirectory = _paths.GetBackupsDirectory();
            Directory.CreateDirectory(backupDirectory);
            var baseName = Path.GetFileNameWithoutExtension(source);
            var target = Path.Combine(backupDirectory, $"Imported_{DateTime.Now:yyyy-MM-dd_HHmmss}_{baseName}.db");
            File.Copy(source, target, overwrite: false);

            var info = new FileInfo(target);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Backups.Add(new AIWordPressManager.Domain.Entities.BackupRecord(target, info.Length, true, DateTime.UtcNow));
            await db.SaveChangesAsync();

            ProgressPercent = 100;
            CurrentStep = "Imported backup ready";
            StatusMessage = $"Imported and verified: {target}";
            await LoadAsyncInternal();
            await _dialogs.ShowInformationAsync("Backup imported", $"The backup was verified and added to the local recovery list.\n\n{target}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            await _dialogs.ShowErrorAsync("Import failed", ex.ToString());
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
            CurrentStep = "Ready";
        }
    }

    private void ExportSelected()
    {
        if (SelectedItem is null || !File.Exists(SelectedItem.FilePath)) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save database backup copy",
            FileName = SelectedItem.FileName,
            DefaultExt = ".db",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;
        File.Copy(SelectedItem.FilePath, dialog.FileName, overwrite: true);
        StatusMessage = $"Backup copy saved to {dialog.FileName}";
    }

    private static async Task VerifyImportedDatabaseAsync(string path)
    {
        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
        }.ToString();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await command.ExecuteScalarAsync());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SQLite integrity check failed: {result}");
    }

    private async Task PrepareRestoreAsync(string backupFilePath)
    {
        if (IsBusy) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Restore database",
            "Restoring replaces the current local SQLite database. A verified safety backup will be created first, then the application will close, restore the selected database, and restart automatically.\n\nContinue?");
        if (!confirmed) return;

        IsBusy = true;
        ProgressPercent = 10;
        CurrentStep = "Validating restore source";
        StatusMessage = $"Checking {backupFilePath}";

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            var executablePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("The application executable path could not be resolved.");

            ProgressPercent = 30;
            CurrentStep = "Creating safety backup";

            var plan = await service.PrepareRestoreAsync(
                backupFilePath,
                Environment.ProcessId,
                executablePath);

            ProgressPercent = 85;
            CurrentStep = "Scheduling restore and restart";
            StatusMessage = $"Restore prepared. Safety backup: {plan.SafetyBackupPath}";

            var launched = Process.Start(new ProcessStartInfo
            {
                FileName = plan.RestoreScriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (launched is null)
            {
                throw new InvalidOperationException("Windows could not start the database restore task.");
            }

            await _dialogs.ShowInformationAsync(
                "Restore scheduled",
                $"The database passed its integrity check.\n\nA safety backup was created at:\n{plan.SafetyBackupPath}\n\nThe application will now close, restore the selected database, and restart automatically.");

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            await _dialogs.ShowErrorAsync("Restore failed", ex.ToString());
        }
        finally
        {
            IsBusy = false;
            ProgressPercent = 100;
            CurrentStep = "Ready";
        }
    }

    private void OpenFolder() => OpenPath(_paths.GetBackupsDirectory());
    private void OpenSelected()
    {
        if (SelectedItem is null) return;
        OpenPath(SelectedItem.FilePath, selectFile: true);
    }

    private static void OpenPath(string path, bool selectFile = false)
    {
        if (selectFile && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(VerifiedCount));
        OnPropertyChanged(nameof(TotalBytes));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(LatestBackupText));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record BackupRow(Guid Id, string FilePath, long FileSizeBytes, bool IsVerified, DateTime CreatedAtUtc)
{
    public string FileName => Path.GetFileName(FilePath);
    public string SizeText
    {
        get
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = FileSizeBytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.##} {units[unit]}";
        }
    }
    public string CreatedAtLocalText => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string VerificationText => IsVerified ? "Verified" : "Unverified";
}
