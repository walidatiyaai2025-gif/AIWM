using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private IAsyncRelayCommand? _resetOperationalDatabaseCommand;
        private string _databaseResetStatus = "Operational data has not been reset.";

        public IAsyncRelayCommand ResetOperationalDatabaseCommand =>
            _resetOperationalDatabaseCommand ??= new AsyncRelayCommand(ResetOperationalDatabaseAsync, () => !IsOperationRunning);

        public string DatabaseResetStatus
        {
            get => _databaseResetStatus;
            private set => SetProperty(ref _databaseResetStatus, value);
        }

        private async Task ResetOperationalDatabaseAsync()
        {
            var firstConfirmation = await _dialogService.ConfirmAsync(
                "Reset operational database",
                "This will permanently remove synchronized WordPress data, audits, suggestions, jobs, logs, backups metadata, reports, and cached workspace data. Users, roles, permissions, sessions, and authentication settings will be preserved. A full database backup will be created first. Continue?");
            if (!firstConfirmation) return;

            var secondConfirmation = await _dialogService.ConfirmAsync(
                "Final confirmation required",
                "This action cannot be undone from the application except by restoring the automatic backup. Confirm that you want to reset all operational data while preserving user security data.");
            if (!secondConfirmation) return;

            IsOperationRunning = true;
            OperationTitle = "Resetting operational database";
            OperationStep = "Creating safety backup";
            OperationProgress = 5;
            OperationDetail = "Preserving users, roles, permissions, sessions, and authentication configuration.";
            ResetOperationalDatabaseCommand.NotifyCanExecuteChanged();

            var databasePath = _applicationPaths.GetDatabasePath();
            if (!File.Exists(databasePath))
            {
                DatabaseResetStatus = "Database file was not found; no reset was performed.";
                IsOperationRunning = false;
                ResetOperationalDatabaseCommand.NotifyCanExecuteChanged();
                await _dialogService.ShowInformationAsync("Database reset", DatabaseResetStatus);
                return;
            }

            var backupDirectory = Path.Combine(_applicationPaths.GetApplicationDataDirectory(), "Backups", "DatabaseReset");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, $"before-reset-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            try
            {
                SqliteConnection.ClearAllPools();
                File.Copy(databasePath, backupPath, overwrite: false);
                OperationProgress = 20;
                OperationStep = "Inspecting database schema";

                await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite");
                await connection.OpenAsync();

                var tables = new List<string>();
                await using (var schemaCommand = connection.CreateCommand())
                {
                    schemaCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                    await using var reader = await schemaCommand.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        tables.Add(reader.GetString(0));
                }

                static bool Preserve(string table)
                {
                    string[] securityMarkers =
                    [
                        "user", "role", "permission", "claim", "login", "session", "identity",
                        "credential", "auth", "account", "membership", "profile", "security"
                    ];
                    return securityMarkers.Any(marker => table.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
                           table.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase);
                }

                var resetTables = tables.Where(table => !Preserve(table)).ToArray();
                OperationProgress = 35;
                OperationStep = "Clearing operational tables";
                OperationDetail = $"Resetting {resetTables.Length} operational table(s); security tables remain untouched.";

                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
                await using (var foreignKeysOff = connection.CreateCommand())
                {
                    foreignKeysOff.Transaction = transaction;
                    foreignKeysOff.CommandText = "PRAGMA defer_foreign_keys = ON;";
                    await foreignKeysOff.ExecuteNonQueryAsync();
                }

                for (var index = 0; index < resetTables.Length; index++)
                {
                    var table = resetTables[index];
                    await using var deleteCommand = connection.CreateCommand();
                    deleteCommand.Transaction = transaction;
                    deleteCommand.CommandText = $"DELETE FROM \"{table.Replace("\"", "\"\"")}\";";
                    await deleteCommand.ExecuteNonQueryAsync();
                    OperationProgress = 35 + (int)Math.Round((index + 1) * 50d / Math.Max(1, resetTables.Length));
                    OperationDetail = $"Cleared {table} ({index + 1} of {resetTables.Length}).";
                }

                await transaction.CommitAsync();
                OperationProgress = 90;
                OperationStep = "Optimizing SQLite";
                await using (var vacuum = connection.CreateCommand())
                {
                    vacuum.CommandText = "VACUUM;";
                    await vacuum.ExecuteNonQueryAsync();
                }

                OperationProgress = 100;
                OperationStep = "Completed";
                DatabaseResetStatus = $"Operational data reset completed at {DateTime.Now:g}. Backup: {backupPath}";
                ApplicationDataStatus = "Operational data was reset. Select a site and synchronize to rebuild the workspace.";
                await _dialogService.ShowInformationAsync(
                    "Database reset completed",
                    $"Operational data was cleared successfully.\n\nPreserved: users, roles, permissions, sessions, credentials, authentication settings, and migrations.\n\nBackup created at:\n{backupPath}\n\nRestart the application before continuing.");
            }
            catch (Exception exception)
            {
                DatabaseResetStatus = $"Reset failed: {exception.Message}";
                OperationDetail = DatabaseResetStatus;
                await _dialogService.ShowErrorAsync("Database reset failed", exception.ToString());
            }
            finally
            {
                IsOperationRunning = false;
                OperationStep = "Ready";
                ResetOperationalDatabaseCommand.NotifyCanExecuteChanged();
            }
        }
    }
}

namespace AIWordPressManager.Desktop
{
    internal static class SystemAdministrationBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(InstallSystemAdministrationTools));
        }

        private static void InstallSystemAdministrationTools(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;

            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var systemTab = FindVisualChild<TabItem>(window, item => string.Equals(item.Header?.ToString(), "SYSTEM", StringComparison.Ordinal));
                if (systemTab?.Content is not StackPanel panel || panel.Children.OfType<Border>().Any(x => x.Tag?.ToString() == "AdministrationTools")) return;

                var group = new Border
                {
                    Tag = "AdministrationTools",
                    Margin = new Thickness(4, 0, 4, 0),
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = window.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
                    Background = window.TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent
                };

                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                var resetButton = new Button
                {
                    Content = "⟲\nReset Data",
                    MinWidth = 86,
                    MinHeight = 58,
                    Padding = new Thickness(10, 6, 10, 6),
                    ToolTip = "Create a full backup, then clear operational data while preserving users, roles, permissions, sessions, and authentication settings."
                };
                if (window.TryFindResource("RibbonLargeButtonStyle") is Style buttonStyle)
                    resetButton.Style = buttonStyle;
                resetButton.SetBinding(Button.CommandProperty, new Binding("ResetOperationalDatabaseCommand"));
                stack.Children.Add(resetButton);
                group.Child = stack;
                panel.Children.Add(group);
            }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed && predicate(typed)) return typed;
                var nested = FindVisualChild(child, predicate);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
