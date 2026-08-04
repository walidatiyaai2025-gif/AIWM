using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class SiteRegistrationJourneyContinuation
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

        viewModel.Sites.Wizard.SiteSaved += async (_, _) =>
        {
            await window.Dispatcher.InvokeAsync(async () =>
            {
                viewModel.Operations.Start(
                    "Preparing your website",
                    "Refreshing the saved website",
                    "Selecting the new website and preparing its first WordPress synchronization.",
                    10);

                try
                {
                    await viewModel.Sites.LoadAsync();

                    viewModel.Operations.Report(
                        20,
                        "Website connected",
                        "Credentials were saved locally. Opening WordPress Explorer.");

                    viewModel.NavigateCommand.Execute("WordPress Explorer");
                    await Task.Delay(150);

                    viewModel.Operations.Report(
                        30,
                        "Starting first synchronization",
                        "Reading posts, pages, categories, tags and media from WordPress.");

                    await viewModel.Explorer.SynchronizeNowAsync();

                    if (!string.Equals(viewModel.Explorer.CurrentOperation, "Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        viewModel.Operations.Fail(
                            "The website was saved, but its first synchronization did not complete. " +
                            viewModel.Explorer.StatusMessage);
                        return;
                    }

                    viewModel.Operations.Report(
                        90,
                        "Verifying local website snapshot",
                        viewModel.Explorer.LastSyncSummary);

                    viewModel.RefreshCompleteUserJourney();

                    viewModel.Operations.Complete(
                        "Your website is connected and its first synchronization is complete. " +
                        "The local SQLite snapshot is ready. The next recommended action is Run AI Audit.");
                }
                catch (Exception ex)
                {
                    viewModel.Operations.Fail(
                        "The website was saved, but the first synchronization could not complete: " + ex.Message);
                }
            }, DispatcherPriority.ContextIdle);
        };
    }
}
