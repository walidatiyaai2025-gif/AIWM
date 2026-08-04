using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop
{
    internal static class HiddenSiteRecoveryExperience
    {
        private static readonly ConditionalWeakTable<MainWindow, object> AttachedWindows = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnMainWindowLoaded),
                true);
        }

        private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
                return;

            if (AttachedWindows.TryGetValue(window, out _))
                return;

            AttachedWindows.Add(window, new object());
            if (window.DataContext is not MainWindowViewModel viewModel)
                return;

            viewModel.Sites.Wizard.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName != nameof(viewModel.Sites.Wizard.ValidationMessage))
                    return;

                var message = viewModel.Sites.Wizard.ValidationMessage;
                if (string.IsNullOrWhiteSpace(message) ||
                    !message.Contains("already registered", StringComparison.OrdinalIgnoreCase))
                    return;

                await viewModel.RestoreHiddenSiteAndReloadAsync(viewModel.Sites.Wizard.SiteUrl);
            };
        }
    }
}

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        internal async Task RestoreHiddenSiteAndReloadAsync(string? siteUrl)
        {
            if (string.IsNullOrWhiteSpace(siteUrl))
                return;

            var normalized = NormalizeSiteAuthority(siteUrl);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            var databasePath = _applicationPaths.GetDatabasePath();
            if (!File.Exists(databasePath))
                return;

            Operations.Start(
                "Reinitializing website",
                "Creating a safety backup",
                "The removed website will be restored and prepared as a newly registered website.",
                5);

            try
            {
                var backupDirectory = Path.Combine(
                    _applicationPaths.GetApplicationDataDirectory(),
                    "Backups",
                    "SiteReinitialization");
                Directory.CreateDirectory(backupDirectory);
                var backupPath = Path.Combine(
                    backupDirectory,
                    $"before-site-reinitialize-{DateTime.Now:yyyyMMdd-HHmmss}.db");

                SqliteConnection.ClearAllPools();
                File.Copy(databasePath, backupPath, overwrite: false);

                Operations.Report(
                    15,
                    "Locating the existing website",
                    "Reading the hidden website record and its previous operational data.");

                await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite");
                await connection.OpenAsync();

                await using (var foreignKeysOff = connection.CreateCommand())
                {
                    foreignKeysOff.CommandText = "PRAGMA foreign_keys = OFF;";
                    await foreignKeysOff.ExecuteNonQueryAsync();
                }

                string? siteId;
                await using (var findSite = connection.CreateCommand())
                {
                    findSite.CommandText = """
                        SELECT Id
                          FROM Sites
                         WHERE lower(rtrim(SiteUrl, '/')) = lower(rtrim($siteUrl, '/'))
                         LIMIT 1;
                        """;
                    findSite.Parameters.AddWithValue("$siteUrl", normalized);
                    siteId = Convert.ToString(await findSite.ExecuteScalarAsync());
                }

                if (string.IsNullOrWhiteSpace(siteId))
                    return;

                Operations.Report(
                    30,
                    "Clearing previous website state",
                    "Removing old synchronization snapshots, audits, suggestions, jobs, approvals, evidence and cached website data.");

                var siteScopedTables = await FindSiteScopedOperationalTablesAsync(connection);
                await using var transaction = await connection.BeginTransactionAsync();

                foreach (var table in siteScopedTables)
                {
                    await using var delete = connection.CreateCommand();
                    delete.Transaction = (SqliteTransaction)transaction;
                    delete.CommandText = $"DELETE FROM {QuoteIdentifier(table)} WHERE SiteId = $siteId;";
                    delete.Parameters.AddWithValue("$siteId", siteId);
                    await delete.ExecuteNonQueryAsync();
                }

                Operations.Report(
                    55,
                    "Resetting website registration state",
                    "Clearing discovery, connection and deletion status so onboarding starts from the beginning.");

                await using (var resetSite = connection.CreateCommand())
                {
                    resetSite.Transaction = (SqliteTransaction)transaction;
                    resetSite.CommandText = """
                        UPDATE Sites
                           SET IsDeleted = 0,
                               DeletedAtUtc = NULL,
                               HomeUrl = NULL,
                               WordPressVersion = NULL,
                               LanguageCode = NULL,
                               ConnectionStatus = 0,
                               LastConnectionTestAtUtc = NULL,
                               UpdatedAtUtc = $updatedAt
                         WHERE Id = $siteId;
                        """;
                    resetSite.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
                    resetSite.Parameters.AddWithValue("$siteId", siteId);
                    await resetSite.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();

                await using (var foreignKeysOn = connection.CreateCommand())
                {
                    foreignKeysOn.CommandText = "PRAGMA foreign_keys = ON;";
                    await foreignKeysOn.ExecuteNonQueryAsync();
                }

                Operations.Report(
                    70,
                    "Reloading the website",
                    "Refreshing the Sites screen and selecting the reinitialized website.");

                await Sites.LoadAsync();
                var existing = Sites.Sites.FirstOrDefault(site =>
                    string.Equals(
                        NormalizeSiteAuthority(site.SiteUrl),
                        normalized,
                        StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                    throw new InvalidOperationException("The website was reinitialized but could not be loaded into the Sites screen.");

                await Sites.SelectSiteCommand.ExecuteAsync(existing);
                Sites.Wizard.IsOpen = false;
                await NavigateCommand.ExecuteAsync("WordPress Explorer");

                Operations.Report(
                    80,
                    "Starting first synchronization",
                    "Reading the website again as a fresh registration and rebuilding the local SQLite snapshot.");

                await Explorer.SynchronizeNowAsync();

                if (Explorer.ProgressPercent < 100 ||
                    !string.Equals(Explorer.CurrentOperation, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    Operations.Fail(
                        "The website was reinitialized successfully, but the first synchronization did not complete. " +
                        Explorer.StatusMessage);
                    return;
                }

                Operations.Report(
                    95,
                    "Rebuilding the user journey",
                    "Recalculating the website stage from the new synchronization result.");

                RefreshCompleteUserJourney();
                Operations.Complete(
                    "The website was restored and fully reinitialized as a new registration. Previous operational data was cleared, a safety backup was created, and the first synchronization completed successfully.");
            }
            catch (Exception exception)
            {
                Operations.Fail(
                    "The website could not be reinitialized. No user accounts or permissions were changed. " +
                    GetInnermostMessage(exception));
                Sites.Wizard.ValidationMessage =
                    "The website exists in the local database but could not be reinitialized. " +
                    GetInnermostMessage(exception);
            }
        }

        private static async Task<IReadOnlyList<string>> FindSiteScopedOperationalTablesAsync(
            SqliteConnection connection)
        {
            var tables = new List<string>();
            await using var schema = connection.CreateCommand();
            schema.CommandText = """
                SELECT name
                  FROM sqlite_master
                 WHERE type = 'table'
                   AND name NOT LIKE 'sqlite_%'
                   AND name <> 'Sites'
                   AND name <> '__EFMigrationsHistory'
                 ORDER BY name;
                """;

            await using var reader = await schema.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (IsSecurityOrCredentialTable(table))
                    continue;

                if (await HasColumnAsync(connection, table, "SiteId"))
                    tables.Add(table);
            }

            return tables;
        }

        private static async Task<bool> HasColumnAsync(
            SqliteConnection connection,
            string table,
            string column)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)});";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsSecurityOrCredentialTable(string table)
        {
            string[] protectedMarkers =
            [
                "user", "role", "permission", "claim", "login", "session",
                "identity", "credential", "secret", "auth", "account", "membership"
            ];

            return protectedMarkers.Any(marker =>
                table.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private static string QuoteIdentifier(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";

        private static string NormalizeSiteAuthority(string? value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
                return (value ?? string.Empty).Trim().TrimEnd('/');

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
                Path = string.Empty,
                Host = uri.Host.ToLowerInvariant()
            };

            if ((builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && builder.Port == 443) ||
                (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && builder.Port == 80))
                builder.Port = -1;

            return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static string GetInnermostMessage(Exception exception)
        {
            var current = exception;
            while (current.InnerException is not null)
                current = current.InnerException;
            return current.Message;
        }
    }
}
