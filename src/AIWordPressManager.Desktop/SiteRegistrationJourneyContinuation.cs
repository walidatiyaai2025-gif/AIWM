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
                    "Website connected",
                    "Preparing the first synchronization",
                    "Refreshing the saved site and opening WordPress Explorer.",
                    25);

                try
                {
                    await viewModel.Sites.LoadAsync();

                    viewModel.Operations.Report(
                        70,
                        "Opening synchronization",
                        "The site is registered. The next required stage is the first WordPress synchronization.");

                    viewModel.NavigateCommand.Execute("WordPress Explorer");
                    viewModel.RefreshCompleteUserJourney();

                    viewModel.Operations.Complete(
                        "Website registration is complete. Start the first synchronization to load posts, pages, media, taxonomy, theme and plugin data into SQLite.");
                }
                catch (Exception ex)
                {
                    viewModel.Operations.Fail(
                        "The website was saved, but the next journey stage could not open automatically: " + ex.Message);
                }
            }, DispatcherPriority.ContextIdle);
        };
    }
}
