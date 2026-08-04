using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class SitesHomeExperience
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> AttachedCards = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Border),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnBorderLoaded),
            true);
    }

    private static void OnBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card || card.DataContext is not SiteCardViewModel site)
            return;

        if (AttachedCards.TryGetValue(card, out _))
            return;

        var mainWindow = FindAncestor<MainWindow>(card);
        if (mainWindow?.DataContext is not MainWindowViewModel main)
            return;

        AttachedCards.Add(card, new object());
        card.Cursor = Cursors.Hand;
        card.ToolTip = BuildTooltip(site);
        card.ContextMenu = BuildContextMenu(main, site);
        card.PreviewMouseLeftButtonUp += async (_, args) =>
        {
            if (args.OriginalSource is Button or Hyperlink)
                return;

            await main.Sites.SelectSiteCommand.ExecuteAsync(site);
        };
    }

    private static ContextMenu BuildContextMenu(MainWindowViewModel main, SiteCardViewModel site)
    {
        var menu = new ContextMenu();
        menu.Opened += async (_, _) =>
        {
            await main.Sites.SelectSiteCommand.ExecuteAsync(site);
            menu.Items.Clear();

            menu.Items.Add(CreateHeader(site));
            menu.Items.Add(new Separator());

            if (!site.IsConnected)
            {
                menu.Items.Add(CreateItem("Retest WordPress connection", async () =>
                    await main.Sites.RetestSelectedSiteCommand.ExecuteAsync(null)));
                menu.Items.Add(CreateItem("Open WordPress admin", () =>
                    main.Sites.OpenWordPressAdminCommand.Execute(null)));
            }
            else
            {
                menu.Items.Add(CreateItem("Open website workspace", async () =>
                {
                    await main.Sites.SelectSiteCommand.ExecuteAsync(site);
                    await main.NavigateCommand.ExecuteAsync("Dashboard");
                }));
                menu.Items.Add(CreateItem("Start / review synchronization", async () =>
                {
                    await main.Sites.SelectSiteCommand.ExecuteAsync(site);
                    await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
                }));
                menu.Items.Add(CreateItem("Run AI / SEO audit", async () =>
                {
                    await main.Sites.SelectSiteCommand.ExecuteAsync(site);
                    await main.NavigateCommand.ExecuteAsync("SEO Audit");
                }));
                menu.Items.Add(CreateItem("Review suggested changes", async () =>
                {
                    await main.Sites.SelectSiteCommand.ExecuteAsync(site);
                    await main.NavigateCommand.ExecuteAsync("Suggested Changes");
                }));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateItem("Open public website", () =>
                main.Sites.OpenSelectedSiteCommand.Execute(null)));
            menu.Items.Add(CreateItem("Copy website URL", () =>
                main.Sites.CopySelectedUrlCommand.Execute(null)));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateItem("Remove from application", async () =>
                await main.Sites.DeleteSelectedSiteCommand.ExecuteAsync(null)));
        };

        return menu;
    }

    private static MenuItem CreateHeader(SiteCardViewModel site) => new()
    {
        Header = $"{site.Name}  •  {site.StatusLabel}",
        IsEnabled = false,
        FontWeight = FontWeights.Bold
    };

    private static MenuItem CreateItem(string header, Action execute)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => execute();
        return item;
    }

    private static MenuItem CreateItem(string header, Func<Task> execute)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await execute();
        return item;
    }

    private static object BuildTooltip(SiteCardViewModel site)
    {
        var panel = new StackPanel { MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = site.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 14
        });
        panel.Children.Add(new TextBlock { Text = site.DisplayHost, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = site.JourneyState, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Next: " + site.RecommendedAction, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Click to select • Right-click for actions", Margin = new Thickness(0, 8, 0, 0) });
        return panel;
    }

    private static T? FindAncestor<T>(DependencyObject? value) where T : DependencyObject
    {
        var current = value;
        while (current is not null)
        {
            if (current is T result)
                return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
