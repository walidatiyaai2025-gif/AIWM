using System.Windows;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

public partial class App
{
    private bool _guidedTourLaunchChecked;

    private void App_OnActivated(object? sender, EventArgs e)
    {
        if (_guidedTourLaunchChecked)
            return;

        if (MainWindow is not MainWindow mainWindow || !mainWindow.IsVisible)
            return;

        _guidedTourLaunchChecked = true;
        var state = GuidedTourStateStore.Load();
        if (state.Completed)
            return;

        if (mainWindow.DataContext is not MainWindowViewModel viewModel)
            return;

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(() =>
            {
                if (!mainWindow.IsVisible)
                    return;

                var tour = new GuidedTourWindow(mainWindow, viewModel);
                tour.Show();
                tour.Activate();
            }));
    }

    public static void ShowGuidedTour(bool restart = false)
    {
        if (Current?.MainWindow is not MainWindow mainWindow ||
            mainWindow.DataContext is not MainWindowViewModel viewModel)
            return;

        if (restart)
            GuidedTourStateStore.Reset();

        foreach (Window window in Current.Windows)
        {
            if (window is GuidedTourWindow existing)
            {
                existing.Activate();
                return;
            }
        }

        var tour = new GuidedTourWindow(mainWindow, viewModel);
        tour.Show();
        tour.Activate();
    }
}
