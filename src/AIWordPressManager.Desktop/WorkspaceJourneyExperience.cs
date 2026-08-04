using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class WorkspaceJourneyExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;
        if (root.RowDefinitions.Count < 4) return;

        Attached.Add(window, new object());

        var previous = root.Children.OfType<UIElement>().Where(x => Grid.GetRow(x) == 2).ToArray();
        foreach (var child in previous) root.Children.Remove(child);

        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(host, 2);
        root.Children.Add(host);
        root.RowDefinitions[2].Height = GridLength.Auto;

        foreach (var child in previous)
        {
            Grid.SetRow(child, 0);
            host.Children.Add(child);
        }

        var header = CreateHeader(main);
        Grid.SetRow(header, 1);
        host.Children.Add(header);

        var journey = CreateJourney(main);
        Grid.SetRow(journey, 2);
        host.Children.Add(journey);

        void Refresh() => RefreshUi(header, journey, main);
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border CreateHeader(MainWindowViewModel main)
    {
        var border = new Border
        {
            Margin = new Thickness(10, 5, 10, 3), Padding = new Thickness(14, 9, 14, 9),
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        border.Child = grid;

        var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0),
            Background = ResourceBrush("PrimaryBrush", Brushes.Teal),
            Child = new TextBlock
            {
                Text = "W", Tag = "WorkspaceInitial", Foreground = Brushes.White, FontWeight = FontWeights.Bold,
                FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "Select a website workspace", Tag = "WorkspaceName", FontSize = 16,
            FontWeight = FontWeights.Bold, Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        text.Children.Add(new TextBlock
        {
            Text = "Choose a site card to begin the guided workflow.", Tag = "WorkspaceSummary",
            Margin = new Thickness(0, 3, 0, 0), Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        identity.Children.Add(text);
        grid.Children.Add(identity);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        actions.Children.Add(Button("Sync now", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            if (main.Sites.SelectedSite is not null) await main.Explorer.SynchronizeNowAsync();
        }));
        actions.Children.Add(Button("AI audit", async () => await main.NavigateCommand.ExecuteAsync("SEO Audit")));
        actions.Children.Add(Button("Open site", () => main.Sites.OpenSelectedSiteCommand.Execute(null)));
        actions.Children.Add(Button("WP Admin", () => main.Sites.OpenWordPressAdminCommand.Execute(null)));

        var developerInfo = CreateDeveloperInfo();
        actions.Children.Add(Button("Developer", () => developerInfo.Visibility =
            developerInfo.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        actions.Children.Add(developerInfo);
        grid.Children.Add(actions);
        return border;
    }

    private static Border CreateJourney(MainWindowViewModel main)
    {
        var border = new Border
        {
            Margin = new Thickness(10, 0, 10, 5), Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray)
        };
        var panel = new UniformGrid { Rows = 1, Columns = 8 };
        border.Child = panel;
        Step(panel, main, "Website", "Sites");
        Step(panel, main, "Connection", "Sites");
        Step(panel, main, "Initial Sync", "WordPress Explorer");
        Step(panel, main, "AI Analysis", "SEO Audit");
        Step(panel, main, "Review", "Suggested Changes");
        Step(panel, main, "Approval", "Approval Queue");
        Step(panel, main, "Execute", "Execution Center");
        Step(panel, main, "Verify", "Evidence Center");
        return border;
    }

    private static void Step(Panel panel, MainWindowViewModel main, string title, string destination)
    {
        var button = new Button
        {
            Content = $"○  {title}", Tag = title, Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(6, 5, 6, 5), ToolTip = $"Open {title}"
        };
        button.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync(destination);
        panel.Children.Add(button);
    }

    private static Border CreateDeveloperInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "Unknown";
        var file = Environment.ProcessPath ?? assembly.Location;
        var built = File.Exists(file) ? File.GetLastWriteTime(file).ToString("g") : "Unknown";
        return new Border
        {
            Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5),
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.Gray),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = new TextBlock
            {
                Text = $"v{version} • Build {built} • {Environment.OSVersion.VersionString}",
                FontSize = 10, Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
            }
        };
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void RefreshUi(Border header, Border journey, MainWindowViewModel main)
    {
        var site = main.Sites.SelectedSite;
        var details = main.Sites.SelectedSiteDetails;
        var name = Find<TextBlock>(header, "WorkspaceName");
        var summary = Find<TextBlock>(header, "WorkspaceSummary");
        var initial = Find<TextBlock>(header, "WorkspaceInitial");

        if (site is null)
        {
            if (name is not null) name.Text = "Select a website workspace";
            if (summary is not null) summary.Text = "Choose a site card to begin the guided workflow.";
            if (initial is not null) initial.Text = "W";
        }
        else
        {
            if (name is not null) name.Text = site.Name;
            if (initial is not null) initial.Text = string.IsNullOrWhiteSpace(site.Name) ? "W" : site.Name[..1].ToUpperInvariant();
            if (summary is not null)
            {
                var wp = string.IsNullOrWhiteSpace(details?.WordPressVersion)
                    ? "WordPress version pending"
                    : $"WordPress {details.WordPressVersion}";
                summary.Text = $"{site.DisplayHost} • {site.StatusLabel} • {wp} • Last test: {site.LastTestText}";
            }
        }

        if (journey.Child is not Panel panel) return;
        var connected = site?.IsConnected == true;
        var synced = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var completed = new[] { site is not null, connected, synced, false, false, false, false, false };
        for (var i = 0; i < panel.Children.Count && i < completed.Length; i++)
        {
            if (panel.Children[i] is not Button button) continue;
            var title = button.Tag?.ToString() ?? string.Empty;
            var current = !completed[i] && completed.Take(i).All(x => x);
            button.Content = completed[i] ? $"✓  {title}" : current ? $"▶  {title}" : $"○  {title}";
            button.FontWeight = current ? FontWeights.Bold : FontWeights.Normal;
            button.Opacity = site is null && i > 0 ? 0.55 : 1;
        }
    }

    private static T? Find<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && Equals(element.Tag, tag)) return element;
            var nested = Find<T>(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
