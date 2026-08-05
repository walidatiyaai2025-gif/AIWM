using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal enum DuplicateSiteDecision
{
    Cancel,
    OpenExisting,
    SoftDelete,
    PhysicalDelete
}

internal static class DuplicateSiteDatabaseDecisionExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();
    private static bool _dialogOpen;

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded), true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window) || Attached.TryGetValue(window, out _))
            return;

        Attached.Add(window, new object());
        if (window.DataContext is not MainWindowViewModel main)
            return;

        main.Sites.Wizard.PropertyChanged += async (_, args) =>
            await OnWizardChangedAsync(window, main, main.Sites.Wizard, args);
    }

    private static async Task OnWizardChangedAsync(
        MainWindow owner,
        MainWindowViewModel main,
        AddSiteWizardViewModel wizard,
        PropertyChangedEventArgs args)
    {
        if (_dialogOpen || args.PropertyName != nameof(AddSiteWizardViewModel.ValidationMessage) ||
            string.IsNullOrWhiteSpace(wizard.ValidationMessage) ||
            !wizard.ValidationMessage.Contains("already registered", StringComparison.OrdinalIgnoreCase))
            return;

        var existing = main.Sites.Sites.FirstOrDefault(x =>
            string.Equals(NormalizeUrl(x.SiteUrl), NormalizeUrl(wizard.SiteUrl), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            await main.Sites.LoadAsync();
            existing = main.Sites.Sites.FirstOrDefault(x =>
                string.Equals(NormalizeUrl(x.SiteUrl), NormalizeUrl(wizard.SiteUrl), StringComparison.OrdinalIgnoreCase));
        }
        if (existing is null)
            return;

        _dialogOpen = true;
        try
        {
            var decision = new DuplicateSiteDecisionWindow(existing.Name, existing.SiteUrl) { Owner = owner }.ShowDialogDecision();
            switch (decision)
            {
                case DuplicateSiteDecision.OpenExisting:
                    wizard.IsOpen = false;
                    await main.Sites.SelectSiteCommand.ExecuteAsync(existing);
                    await main.NavigateCommand.ExecuteAsync("Sites");
                    break;

                case DuplicateSiteDecision.SoftDelete:
                    await DeleteAsync(owner, main, existing.Id.ToString(), physical: false);
                    wizard.IsOpen = false;
                    break;

                case DuplicateSiteDecision.PhysicalDelete:
                    if (MessageBox.Show(owner,
                            "Physical deletion permanently removes the site and all site-scoped local data. A database backup will be created first. Continue?",
                            "Confirm physical deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    await DeleteAsync(owner, main, existing.Id.ToString(), physical: true);
                    wizard.ValidationMessage = string.Empty;
                    wizard.ConnectionMessage = "The previous local record was physically deleted. You can now save this site as a new registration.";
                    break;
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static async Task DeleteAsync(MainWindow owner, MainWindowViewModel main, string siteId, bool physical)
    {
        var databasePath = owner.Tag as string;
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            throw new InvalidOperationException("The active SQLite database path is unavailable.");

        var backupDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "Backups", "SiteDeletion");
        Directory.CreateDirectory(backupDirectory);
        SqliteConnection.ClearAllPools();
        File.Copy(databasePath, Path.Combine(backupDirectory, $"before-site-delete-{DateTime.Now:yyyyMMdd-HHmmss}.db"));

        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        if (physical)
        {
            await using var schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('Sites','__EFMigrationsHistory');";
            var tables = new List<string>();
            await using (var reader = await schema.ExecuteReaderAsync())
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));

            foreach (var table in tables)
            {
                await using var columns = connection.CreateCommand();
                columns.Transaction = transaction;
                columns.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";
                var hasSiteId = false;
                await using (var reader = await columns.ExecuteReaderAsync())
                    while (await reader.ReadAsync())
                        if (string.Equals(reader.GetString(1), "SiteId", StringComparison.OrdinalIgnoreCase)) hasSiteId = true;
                if (!hasSiteId) continue;

                await using var deleteScoped = connection.CreateCommand();
                deleteScoped.Transaction = transaction;
                deleteScoped.CommandText = $"DELETE FROM \"{table.Replace("\"", "\"\"")}\" WHERE SiteId=$siteId;";
                deleteScoped.Parameters.AddWithValue("$siteId", siteId);
                await deleteScoped.ExecuteNonQueryAsync();
            }

            await using var deleteSite = connection.CreateCommand();
            deleteSite.Transaction = transaction;
            deleteSite.CommandText = "DELETE FROM Sites WHERE Id=$siteId;";
            deleteSite.Parameters.AddWithValue("$siteId", siteId);
            await deleteSite.ExecuteNonQueryAsync();
        }
        else
        {
            await using var softDelete = connection.CreateCommand();
            softDelete.Transaction = transaction;
            softDelete.CommandText = "UPDATE Sites SET IsDeleted=1, DeletedAtUtc=$now, UpdatedAtUtc=$now WHERE Id=$siteId;";
            softDelete.Parameters.AddWithValue("$siteId", siteId);
            softDelete.Parameters.AddWithValue("$now", DateTime.UtcNow);
            await softDelete.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        await main.Sites.LoadAsync();
        MessageBox.Show(owner,
            physical ? "The site and all site-scoped local records were physically deleted." : "The site was soft deleted and can be restored later.",
            "Site deletion completed", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return (value ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant();
    }
}

internal sealed class DuplicateSiteDecisionWindow : Window
{
    private DuplicateSiteDecision _decision;

    internal DuplicateSiteDecisionWindow(string name, string url)
    {
        Title = "Existing WordPress site";
        Width = 610;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "This website already exists in SQLite", FontSize = 24, FontWeight = FontWeights.Bold });
        root.Children.Add(new TextBlock { Text = $"{name}\n{url}", Margin = new Thickness(0, 10, 0, 18), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock
        {
            Text = "Choose exactly what should happen. No automatic synchronization or reinitialization will run before your choice.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        root.Children.Add(CreateButton("Open existing site", "Load the saved record from SQLite and select it.", DuplicateSiteDecision.OpenExisting));
        root.Children.Add(CreateButton("Soft delete", "Hide the site while keeping its local history for later restoration.", DuplicateSiteDecision.SoftDelete));
        root.Children.Add(CreateButton("Physical delete", "Permanently delete the site and all site-scoped local records after creating a backup.", DuplicateSiteDecision.PhysicalDelete));
        root.Children.Add(CreateButton("Cancel", "Make no database changes.", DuplicateSiteDecision.Cancel));
        Content = root;
    }

    internal DuplicateSiteDecision ShowDialogDecision()
    {
        ShowDialog();
        return _decision;
    }

    private Button CreateButton(string title, string description, DuplicateSiteDecision decision)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = description, Opacity = 0.75, TextWrapping = TextWrapping.Wrap }
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 0, 0, 8)
        };
        button.Click += (_, _) => { _decision = decision; DialogResult = true; };
        return button;
    }
}
