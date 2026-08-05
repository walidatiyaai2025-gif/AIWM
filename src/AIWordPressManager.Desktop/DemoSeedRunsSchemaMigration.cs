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
    private static readonly object Gate = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(OnDemoButtonPreviewMouseDown),
            true);
    }

    private static void OnDemoButtonPreviewMouseDown(
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
            Ensure(databasePath);
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

    internal static void Ensure(string databasePath)
    {
        lock (Gate)
        {
            using var connection = new SqliteConnection(
                $"Data Source={databasePath};Default Timeout=30");
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout=30000;";
                pragma.ExecuteNonQuery();
            }

            using (var create = connection.CreateCommand())
            {
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS DemoSeedRuns(
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SiteId TEXT NULL,
                        SeedVersion TEXT NOT NULL,
                        SeededAtUtc TEXT NOT NULL,
                        Summary TEXT NOT NULL);
                    """;
                create.ExecuteNonQuery();
            }

            if (!HasColumn(connection, "DemoSeedRuns", "SiteId"))
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE DemoSeedRuns ADD COLUMN SiteId TEXT NULL;";
                alter.ExecuteNonQuery();
            }

            using var index = connection.CreateCommand();
            index.CommandText = """
                CREATE INDEX IF NOT EXISTS IX_DemoSeedRuns_SiteId
                ON DemoSeedRuns(SiteId);
                """;
            index.ExecuteNonQuery();
        }
    }

    private static bool HasColumn(
        SqliteConnection connection,
        string table,
        string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        while (reader.Read())
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
