using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal static class VerifiedSiteScopedDemoLauncher
{
    private static readonly string[] DemoTables =
    [
        "DemoSites",
        "DemoPosts",
        "DemoCategories",
        "DemoMedia",
        "DemoTags",
        "DemoJobs",
        "DemoNotifications",
        "DemoOperations",
        "DemoSeoAudits",
        "DemoSuggestions"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown),
            true);
    }

    private static async void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: "DemoDataLauncher" } button ||
            Window.GetWindow(button) is not MainWindow window)
        {
            return;
        }

        // This is the only supported launcher path. Prevent older Click handlers
        // from opening a second generator or bypassing schema verification.
        e.Handled = true;

        if (window.DataContext is not MainWindowViewModel main ||
            main.Sites.SelectedSite is not SiteCardViewModel site)
        {
            MessageBox.Show(
                window,
                "Select a WordPress site first. Demo data is always written for the active site.",
                "Demo Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (window.Tag is not string databasePath || string.IsNullOrWhiteSpace(databasePath))
        {
            MessageBox.Show(
                window,
                "The active SQLite database path is not available.",
                "Demo Data",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        button.IsEnabled = false;
        try
        {
            await DemoSeedRunsSchemaMigration.EnsureAsync(databasePath);

            var generator = new SiteScopedDemoDataWindow(
                databasePath,
                site.Id,
                site.Name,
                site.SiteUrl)
            {
                Owner = window
            };

            generator.ShowDialog();

            var verification = await VerifyAsync(databasePath, site.Id);
            var report = BuildReport(site, databasePath, verification);
            var allValid = verification.All(item => item.Exists && item.Count >= SiteScopedDemoSeeder.RecordsPerTable);

            MessageBox.Show(
                window,
                report,
                allValid ? "Demo Data Saved and Verified" : "Demo Data Verification Failed",
                MessageBoxButton.OK,
                allValid ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (allValid)
            {
                main.Operations.Start(
                    "Demo data verified",
                    $"Loading data for {site.Name}",
                    $"SQLite contains {verification.Sum(x => x.Count)} verified demo rows for the active site.",
                    100);
                main.Operations.Complete(
                    "Demo data was committed and verified directly from SQLite. Refresh module screens to display the new records.");
            }
            else
            {
                main.Operations.Fail(
                    "Demo data did not pass post-commit verification. Review the table counts shown in the verification report.");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                window,
                "Demo data could not be saved. No success status was recorded.\n\n" +
                GetInnermostMessage(exception),
                "Demo Data Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static async Task<IReadOnlyList<TableVerification>> VerifyAsync(
        string databasePath,
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<TableVerification>(DemoTables.Length);
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Mode=ReadOnly;Default Timeout=30");
        await connection.OpenAsync(cancellationToken);

        var site = siteId.ToString("D");
        foreach (var table in DemoTables)
        {
            if (!await TableExistsAsync(connection, table, cancellationToken))
            {
                result.Add(new TableVerification(table, false, 0));
                continue;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE IsDemo=1 AND SiteId=$siteId;";
            command.Parameters.AddWithValue("$siteId", site);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            result.Add(new TableVerification(table, true, count));
        }

        return result;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string BuildReport(
        SiteCardViewModel site,
        string databasePath,
        IReadOnlyList<TableVerification> verification)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Site: {site.Name}");
        builder.AppendLine($"Site ID: {site.Id:D}");
        builder.AppendLine($"Database: {databasePath}");
        builder.AppendLine();
        builder.AppendLine("Post-commit SQLite verification:");

        foreach (var item in verification)
        {
            var valid = item.Exists && item.Count >= SiteScopedDemoSeeder.RecordsPerTable;
            builder.AppendLine(
                $"{(valid ? "OK" : "FAILED"),-7} {item.Table,-24} {item.Count}/{SiteScopedDemoSeeder.RecordsPerTable}");
        }

        builder.AppendLine();
        builder.AppendLine($"Verified total: {verification.Sum(x => x.Count)} rows");
        builder.AppendLine(
            verification.All(x => x.Exists && x.Count >= SiteScopedDemoSeeder.RecordsPerTable)
                ? "The transaction is physically stored in SQLite."
                : "One or more tables did not contain the required committed records.");

        return builder.ToString();
    }

    private static string GetInnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }

    private sealed record TableVerification(string Table, bool Exists, int Count);
}
