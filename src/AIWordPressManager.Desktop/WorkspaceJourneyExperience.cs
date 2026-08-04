using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class WorkspaceJourneyExperience
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

        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root)
            return;

        AttachedWindows.Add(window, new object());
        InstallWorkspaceExperience(window, root, main);
    }

    private static void InstallWorkspaceExperience(MainWindow window, Grid root, MainWindowViewModel main)
    {
        if (root.RowDefinitions.Count < 4)
            return;

        var existingRowChildren = root.Children
            .OfType<UIElement>()
            .Where(child => Grid.GetRow(child) == 2)
            .ToArray();

        foreach (var child in existingRowChildren)
            root.Children.Remove(child);

        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(host, 2);
        root.Children.Add(host);
        root.RowDefinitions[2].Height = GridLength.Auto;

        foreach (var child in existingRowChildren)
        {
            Grid.SetRow(child, 0);
            host.Children.Add(child);
        }

        var workspaceHeader = BuildWorkspaceHeader(main);
        Grid.SetRow(workspaceHeader, 1);
        host.Children.Add(workspaceHeader);

        var journeyRibbon = BuildJourneyRibbon(main);
        Grid.SetRow(journeyRibbon, 2);
        host.Children.Add(journeyRibbon);

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) => RefreshWorkspace(workspaceHeader, journeyRibbon, main);
        window.Closed += (_, _) => timer.Stop();
        main.Sites.SelectedSiteChanged += (_, _) => RefreshWorkspace(workspaceHeader, journeyRibbon, main);
        timer.Start();
        RefreshWorkspace(workspaceHeader, journeyRibbon, main);
    }

    private static Border BuildWorkspaceHeader(MainWindowViewModel main)
    {
        var border = new Border
        {
            Margin = new Thickness(10, 5, 10, 3),
            Padding = new Thickness(14, 9, 14, 9),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            Tag = "WorkspaceHeader"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        border.Child = grid;

        var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var badge = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = Brush("PrimaryBrush", Brushes.Teal),
            Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock
            {
                Text = "W",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "WorkspaceInitial"
            }
        };
        identity.Children.Add(badge);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock
        {
            Text = "Select a website workspace",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black),
            Tag = "WorkspaceName"
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "Choose a site card to make it the active workspace.",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray),
            Tag = "WorkspaceSummary"
        });
        identity.Children.Add(titleStack);
        grid.Children.Add(identity);

        var actions = new StackPanel { Grid.ColumnProperty = { }, Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 1);
        actions.Children.Add(ActionButton("Sync now", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            if (main.Sites.SelectedSite is not null)
                await main.Explorer.SynchronizeNowAsync();
        }));
        actions.Children.Add(ActionButton("AI audit", async () => await main.NavigateCommand.ExecuteAsync("SEO Audit")));
        actions.Children.Add(ActionButton("Open site", () => main.Sites.OpenSelectedSiteCommand.Execute(null)));
        actions.Children.Add(ActionButton("WP Admin", () => main.Sites.OpenWordPressAdminCommand.Execute(null)));

        var developerPanel = BuildDeveloperPanel();
        var developerToggle = ActionButton("Developer", () =>
        {
            developerPanel.Visibility = developerPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        });
        actions.Children.Add(developerToggle);
        actions.Children.Add(developerPanel);
        grid.Children.Add(actions);
        return border;
    }

    private static Border BuildJourneyRibbon(MainWindowViewModel main)
    {
        var border = new Border
        {
            Margin = new Thickness(10, 0, 10, 5),
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(8),
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Tag = "JourneyRibbon"
        };

        var panel = new UniformGrid { Rows = 1, Columns = 8 };
        border.Child = panel;
        AddJourneyStep(panel, main, "Website", "Sites");
        AddJourneyStep(panel, main, "Connection", "Sites");
        AddJourneyStep(panel, main, "Initial Sync", "WordPress Explorer");
        AddJourneyStep(panel, main, "AI Analysis", "SEO Audit");
        AddJourneyStep(panel, main, "Review", "Suggested Changes");
        AddJourneyStep(panel, main, "Approval", "Approval Queue");
        AddJourneyStep(panel, main, "Execute", "Execution Center");
        AddJourneyStep(panel, main, "Verify", "Evidence Center");
        return border;
    }

    private static void AddJourneyStep(Panel panel, MainWindowViewModel main, string title, string destination)
    {
        var button = new Button
        {
            Margin = new Thickness(3, 0, 3, 0),
            Padding = new Thickness(6, 5, 6, 5),
            Content = $"○  {title}",
            Tag = title,
            ToolTip = $"Open {title}",
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += async (_, _) => await main.NavigateCommand.ExecuteAsync(destination);
        panel.Children.Add(button);
    }

    private static Border BuildDeveloperPanel()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "Unknown";
        var executablePath = Environment.ProcessPath ?? assembly.Location;
        var buildDate = File.Exists(executablePath)
            ? File.GetLastWriteTime(executablePath).ToString("g")
            : "Unknown";

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(9, 5, 9, 5),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush", Brushes.Gray),
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = new TextBlock
            {
                Text = $"v{version}  •  Build {buildDate}  •  {Environment.OSVersion.VersionString}",
                FontSize = 10,
                Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
            }
        };
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 5, 9, 5) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void RefreshWorkspace(Border header, Border ribbon, MainWindowViewModel main)
    {
        var selected = main.Sites.SelectedSite;
        var details = main.Sites.SelectedSiteDetails;
        var name = FindByTag<TextBlock>(header, "WorkspaceName");
        var summary = FindByTag<TextBlock>(header, "WorkspaceSummary");
        var initial = FindByTag<TextBlock>(header, "WorkspaceInitial");

        if (selected is null)
        {
            if (name is not null) name.Text = "Select a website workspace";
            if (summary is not null) summary.Text = "Choose a site card to begin the guided workflow.";
            if (initial is not null) initial.Text = "W";
        }
        else
        {
            if (name is not null) name.Text = selected.Name;
            if (initial is not null) initial.Text = selected.Name[..1].ToUpperInvariant();
            if (summary is not null)
            {
                var wp = string.IsNullOrWhiteSpace(details?.WordPressVersion) ? "WordPress version pending" : $"WordPress {details.WordPressVersion}";
                summary.Text = $"{selected.DisplayHost}  •  {selected.StatusLabel}  •  {wp}  •  Last test: {selected.LastTestText}";
            }
        }

        if (ribbon.Child is not Panel steps)
            return;

        var connected = selected?.IsConnected == true;
        var synchronized = main.Explorer.LoadedAt.HasValue || main.Explorer.LoadedItemsCount > 0;
        var states = new[]
        {
            selected is not null,
            connected,
            synchronized,
            synchronized,
            synchronized,
            false,
            false,
            false
        };

        for (var index = 0; index < steps.Children.Count && index < states.Length; index++)
        {
            if (steps.Children[index] is not Button button)
                continue;
            var title = button.Tag?.ToString() ?? string.Empty;
            var completed = states[index];
            var isCurrent = !completed && states.Take(index).All(value => value);
            button.Content = completed ? $"✓  {title}" : isCurrent ? $"▶  {title}" : $"○  {title}";
            button.FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
            button.Opacity = selected is null && index > 0 ? 0.55 : 1;
        }
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T element && Equals(element.Tag, tag))
                return element;
            var nested = FindByTag<T>(child, tag);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static Brush Brush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
