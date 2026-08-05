using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Upgrades databases created by older demo-data versions. Older copies of
/// DemoSeedRuns did not contain SiteId, which caused the final audit insert to
/// fail and rolled back every demo row in the transaction.
/// </summary>
internal static class DemoSeedRunsSchemaMigration
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(OnDemoButtonPreviewMouseDown),
            true);
    }

    private static async void OnDemoButtonPreviewMouseDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: "DemoDataLauncher" } button ||
            Window.GetWindow(button) is not MainWindow window ||
            window.Tag is not string databasePath ||
            string.IsNullOrWhiteSpace(databasePath))
        {
            return;
        }

        try
        {
            await EnsureAsync(databasePath);
        }
        catch (Exception exception)
        {
            e.Handled = true;
            MessageBox.Show(
                window,
                "Demo-data database preparation failed. No records were written.\n\n" +
                GetInnermostMessage(exception),
                "Demo Data Schema Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    internal static async Task EnsureAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Default Timeout=30");
            await connection.OpenAsync(cancellationToken);

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout=30000;";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

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
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!await HasColumnAsync(connection, "DemoSeedRuns", "SiteId", cancellationToken))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE DemoSeedRuns ADD COLUMN SiteId TEXT NULL;";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var index = connection.CreateCommand())
            {
                index.CommandText = """
                    CREATE INDEX IF NOT EXISTS IX_DemoSeedRuns_SiteId
                    ON DemoSeedRuns(SiteId);
                    """;
                await index.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> HasColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetInnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }
}
