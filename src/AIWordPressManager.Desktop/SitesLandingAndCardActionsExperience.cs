using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class SitesLandingAndCardActionsExperience
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> ConfiguredCards = new();
    private static readonly ConditionalWeakTable<MainWindow, object> InitializedWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(Border),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnBorderLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (InitializedWindows.TryGetValue(window, out _))
            return;

        InitializedWindows.Add(window, new object());

        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (window.DataContext is not MainWindowViewModel viewModel)
                return;

            viewModel.NavigateCommand.Execute("Sites");
            _ = viewModel.Sites.LoadAsync();
        }));
    }

    private static void OnBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not SiteCardViewModel)
            return;

        if (!IsCardRoot(border) || ConfiguredCards.TryGetValue(border, out _))
            return;

        ConfiguredCards.Add(border, new object());
        border.Cursor = Cursors.Hand;
        border.ToolTip = "Click to select this website. Right-click to open available actions.";
        border.PreviewMouseLeftButtonUp += OnCardLeftClick;
        border.ContextMenuOpening += OnCardContextMenuOpening;
    }

    private static bool IsCardRoot(FrameworkElement element)
    {
        var parent = VisualTreeHelper.GetParent(element) as FrameworkElement;
        return parent?.DataContext is not SiteCardViewModel;
    }

    private static async void OnCardLeftClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SiteCardViewModel site)
            return;

        var viewModel = FindMainViewModel(element);
        if (viewModel is null)
            return;

        await SelectSiteAsync(viewModel, site);
        e.Handled = true;
    }

    private static void OnCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SiteCardViewModel site)
            return;

        var viewModel = FindMainViewModel(element);
        if (viewModel is null)
            return;

        element.ContextMenu = BuildContextMenu(viewModel, site);
    }

    private static ContextMenu BuildContextMenu(MainWindowViewModel viewModel, SiteCardViewModel site)
    {
        var menu = new ContextMenu();

        menu.Items.Add(CreateMenuItem("Select website", async () =>
            await SelectSiteAsync(viewModel, site)));

        menu.Items.Add(CreateMenuItem("Open website", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            viewModel.Sites.OpenSelectedSiteCommand.Execute(null);
        }));

        menu.Items.Add(CreateMenuItem("Open WordPress admin", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            viewModel.Sites.OpenWordPressAdminCommand.Execute(null);
        }));

        menu.Items.Add(new Separator());

        menu.Items.Add(CreateMenuItem("Synchronize website", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            viewModel.NavigateCommand.Execute("WordPress Explorer");
        }));

        menu.Items.Add(CreateMenuItem("Retest connection", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            if (viewModel.Sites.RetestSelectedSiteCommand.CanExecute(null))
                viewModel.Sites.RetestSelectedSiteCommand.Execute(null);
        }));

        menu.Items.Add(CreateMenuItem("Copy website URL", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            viewModel.Sites.CopySelectedUrlCommand.Execute(null);
        }));

        menu.Items.Add(new Separator());

        var delete = CreateMenuItem("Remove from application", async () =>
        {
            await SelectSiteAsync(viewModel, site);
            if (viewModel.Sites.DeleteSelectedSiteCommand.CanExecute(null))
                viewModel.Sites.DeleteSelectedSiteCommand.Execute(null);
        });
        delete.Foreground = Brushes.IndianRed;
        menu.Items.Add(delete);

        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private static async Task SelectSiteAsync(MainWindowViewModel viewModel, SiteCardViewModel site)
    {
        if (!ReferenceEquals(viewModel.Sites.SelectedSite, site))
            await viewModel.Sites.SelectSiteCommand.ExecuteAsync(site);
    }

    private static MainWindowViewModel? FindMainViewModel(DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is MainWindow window)
                return window.DataContext as MainWindowViewModel;

            current = VisualTreeHelper.GetParent(current);
        }

        return global::System.Windows.Application.Current?.Windows
            .OfType<MainWindow>()
            .FirstOrDefault()?.DataContext as MainWindowViewModel;
    }
}
