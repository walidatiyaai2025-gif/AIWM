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

            try
            {
                await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWrite");
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE Sites
                       SET IsDeleted = 0,
                           DeletedAtUtc = NULL,
                           UpdatedAtUtc = $updatedAt
                     WHERE lower(rtrim(SiteUrl, '/')) = lower(rtrim($siteUrl, '/'))
                       AND IsDeleted = 1;
                    """;
                command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow);
                command.Parameters.AddWithValue("$siteUrl", normalized);

                var restored = await command.ExecuteNonQueryAsync();
                if (restored <= 0)
                    return;

                await Sites.LoadAsync();
                var existing = Sites.Sites.FirstOrDefault(site =>
                    string.Equals(
                        NormalizeSiteAuthority(site.SiteUrl),
                        normalized,
                        StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                    await Sites.SelectSiteCommand.ExecuteAsync(existing);

                Sites.Wizard.IsOpen = false;
                await NavigateCommand.ExecuteAsync("Sites");
                RefreshCompleteUserJourney();

                Operations.Start(
                    "Existing website restored",
                    "Website card is available again",
                    "The website existed as a removed local record. It was restored without creating a duplicate.",
                    100);
                Operations.Complete(
                    "The existing website is now visible and selected. Use its card or right-click menu to synchronize, retest, open WordPress admin, or remove it.");
            }
            catch (Exception exception)
            {
                Sites.Wizard.ValidationMessage =
                    "The website exists in the local database but could not be restored to the Sites screen. " +
                    GetInnermostMessage(exception);
            }
        }

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
