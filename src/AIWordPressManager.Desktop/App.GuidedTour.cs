using System.Windows;
using AIWordPressManager.Desktop.Services.Sites;
using AIWordPressManager.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop;

public partial class App
{
    private bool _guidedTourLaunchChecked;
    private bool _siteIsolationBound;
    private ICurrentSiteContext? _boundSiteContext;
    private EventHandler<CurrentSiteChangedEventArgs>? _siteChangedHandler;

    private void App_OnActivated(object? sender, EventArgs e)
    {
        if (MainWindow is not MainWindow mainWindow || !mainWindow.IsVisible)
            return;

        if (mainWindow.DataContext is not MainWindowViewModel viewModel)
            return;

        BindSiteIsolation(viewModel);
        viewModel.BindExecutionReceiptStore();

        if (_guidedTourLaunchChecked)
            return;

        _guidedTourLaunchChecked = true;
        var state = GuidedTourStateStore.Load();
        if (state.Completed)
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

    private void BindSiteIsolation(MainWindowViewModel viewModel)
    {
        if (_siteIsolationBound || _host is null)
            return;

        var siteContext = _host.Services.GetService<ICurrentSiteContext>();
        if (siteContext is null)
            return;

        _siteChangedHandler = (_, args) =>
        {
            if (Dispatcher.CheckAccess())
            {
                viewModel.ExecutionCenter.HandleActiveSiteChanged(args);
                viewModel.Backups.HandleActiveSiteChanged(args);
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                viewModel.ExecutionCenter.HandleActiveSiteChanged(args);
                viewModel.Backups.HandleActiveSiteChanged(args);
            });
        };

        siteContext.CurrentSiteChanged += _siteChangedHandler;
        _boundSiteContext = siteContext;
        _siteIsolationBound = true;
    }

    private void UnbindSiteIsolation()
    {
        if (_boundSiteContext is not null && _siteChangedHandler is not null)
            _boundSiteContext.CurrentSiteChanged -= _siteChangedHandler;

        _boundSiteContext = null;
        _siteChangedHandler = null;
        _siteIsolationBound = false;
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
