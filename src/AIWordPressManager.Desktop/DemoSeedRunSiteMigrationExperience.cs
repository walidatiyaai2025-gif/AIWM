using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal static class DemoSeedRunSiteMigrationExperience
{
    private static bool _completed;

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static async void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_completed || sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (window.Tag is not string databasePath || string.IsNullOrWhiteSpace(databasePath))
            return;

        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Default Timeout=30");
            await connection.OpenAsync();

            await using (var create = connection.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS DemoSeedRuns(
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SiteId TEXT NULL,
                        SeedVersion TEXT NOT NULL,
                        SeededAtUtc TEXT NOT NULL,
                        Summary TEXT NOT NULL);
                    """;
                await create.ExecuteNonQueryAsync();
            }

            var hasSiteId = false;
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = "PRAGMA table_info(DemoSeedRuns);";
                await using var reader = await schema.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "SiteId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasSiteId = true;
                        break;
                    }
                }
            }

            if (!hasSiteId)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE DemoSeedRuns ADD COLUMN SiteId TEXT NULL;";
                await alter.ExecuteNonQueryAsync();
            }

            await using (var index = connection.CreateCommand())
            {
                index.CommandText = "CREATE INDEX IF NOT EXISTS IX_DemoSeedRuns_SiteId ON DemoSeedRuns(SiteId);";
                await index.ExecuteNonQueryAsync();
            }

            _completed = true;
        }
        catch
        {
            // The demo window reports database errors explicitly when the user starts seeding.
        }
    }
}
