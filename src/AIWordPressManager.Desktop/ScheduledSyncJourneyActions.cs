using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class JobsViewModel
    {
        public async Task FocusFailedWordPressSyncAsync(Guid siteId)
        {
            SelectedStatus = "Failed";
            SearchText = "WordPressSync";
            await LoadAsync();

            SelectedItem = Items
                .Where(item => item.SiteId == siteId &&
                               item.JobType.Equals("WordPressSync", StringComparison.OrdinalIgnoreCase) &&
                               item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefault();

            if (SelectedItem is null)
            {
                StatusMessage = "The paused synchronization was detected in the application log, but its failed job record was not found in the current SQLite filter. Review all failed jobs or refresh the queue.";
                return;
            }

            StatusMessage = $"Selected the latest failed WordPressSync job for {SelectedItem.SiteName}. Review Error Details, correct the cause, then use Retry selected.";
        }
    }
}

namespace AIWordPressManager.Desktop
{
    internal static partial class ScheduledSyncJourneyActions
    {
        private static readonly Regex SiteIdRegex = new(
            @"Site\s+(?<site>[0-9a-fA-F-]{36})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.ClickEvent,
                new RoutedEventHandler(OnButtonClick),
                true);
        }

        private static async void OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                !string.Equals(button.Content?.ToString(), "Open Jobs", StringComparison.OrdinalIgnoreCase) ||
                button.DataContext is not ViewModels.MainWindowViewModel viewModel ||
                !viewModel.HasScheduledSyncPause)
                return;

            var match = SiteIdRegex.Match(viewModel.ScheduledSyncPauseDetail);
            if (!match.Success || !Guid.TryParse(match.Groups["site"].Value, out var siteId))
                return;

            e.Handled = true;

            try
            {
                await viewModel.NavigateCommand.ExecuteAsync("Jobs");
                await viewModel.Jobs.FocusFailedWordPressSyncAsync(siteId);
            }
            catch (Exception exception)
            {
                viewModel.Operations.Fail($"Unable to open the paused synchronization job: {exception.Message}");
            }
        }
    }
}
