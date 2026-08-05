using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Converts the main work area into content + a docked right sidebar. The sidebar
/// uses existing navigation commands only and never overlays page content.
/// </summary>
internal static class DockedRightSidebarExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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

        var workChildren = root.Children.OfType<UIElement>()
            .Where(child => Grid.GetRow(child) == 3)
            .ToArray();
        if (workChildren.Length == 0) return;

        var host = new Grid { Tag = "DockedWorkspaceHost" };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        Grid.SetRow(host, 3);

        var contentHost = new Grid { Tag = "DockedWorkspaceContent" };
        Grid.SetColumn(contentHost, 0);
        host.Children.Add(contentHost);

        foreach (var child in workChildren)
        {
            root.Children.Remove(child);
            Grid.SetRow(child, 0);
            contentHost.Children.Add(child);
        }

        var state = new State(window, root, main, host, contentHost);
        var sidebar = BuildSidebar(state);
        state.Sidebar = sidebar;
        Grid.SetColumn(sidebar, 1);
        host.Children.Add(sidebar);
        root.Children.Add(host);

        Attached.Add(window, state);
        state.Attach();
        state.Refresh();
    }

    private static Border BuildSidebar(State state)
    {
        var shell = new Border
        {
            Tag = "DockedRightSidebar",
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Child = layout;

        var header = new Grid { Height = 42, Margin = new Thickness(5, 4, 5, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        state.HeaderText = new TextBlock
        {
            Text = "Workspace",
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Brush("TextPrimaryBrush", Brushes.Black),
            Visibility = Visibility.Collapsed
        };
        header.Children.Add(state.HeaderText);

        var toggle = new Button
        {
            Content = "☰",
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            ToolTip = "Open or close workspace sidebar",
            Focusable = false
        };
        toggle.Click += state.OnToggleClick;
        Grid.SetColumn(toggle, 1);
        header.Children.Add(toggle);
        layout.Children.Add(header);

        var tabs = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(5, 2, 5, 6)
        };
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);

        tabs.Children.Add(TabButton("✓", "Journey", "Dashboard", state));
        tabs.Children.Add(TabButton("✦", "AI Copilot", "AI Studio", state));
        tabs.Children.Add(TabButton("▶", "Operations", "Operations Center", state));
        tabs.Children.Add(TabButton("🔔", "Notifications", "Notification Center", state));
        tabs.Children.Add(TabButton("⚡", "Quick Fix", "Suggested Changes", state));

        state.DetailPanel = new StackPanel
        {
            Margin = new Thickness(12, 8, 12, 12),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(state.DetailPanel, 2);
        layout.Children.Add(state.DetailPanel);

        state.DetailPanel.Children.Add(new TextBlock
        {
            Text = "CURRENT WORKSPACE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        });
        state.PageText = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 4),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        };
        state.DetailPanel.Children.Add(state.PageText);
        state.SiteText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        state.DetailPanel.Children.Add(state.SiteText);
        state.StatusText = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        state.DetailPanel.Children.Add(state.StatusText);

        return shell;
    }

    private static Button TabButton(string icon, string label, string page, State state)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = icon,
            Width = 26,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = label,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "SidebarLabel",
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(text);

        var button = new Button
        {
            Content = panel,
            Height = 34,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(4, 2, 4, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = label,
            Tag = page
        };
        button.Click += state.OnNavigationClick;
        state.NavigationButtons.Add(button);
        state.Labels.Add(text);
        return button;
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed class State(
        MainWindow window,
        Grid root,
        MainWindowViewModel main,
        Grid host,
        Grid contentHost)
    {
        private bool _expanded;

        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
        public Grid Host { get; } = host;
        public Grid ContentHost { get; } = contentHost;
        public Border Sidebar { get; set; } = null!;
        public TextBlock HeaderText { get; set; } = null!;
        public StackPanel DetailPanel { get; set; } = null!;
        public TextBlock PageText { get; set; } = null!;
        public TextBlock SiteText { get; set; } = null!;
        public TextBlock StatusText { get; set; } = null!;
        public List<Button> NavigationButtons { get; } = [];
        public List<TextBlock> Labels { get; } = [];

        public void Attach()
        {
            Main.PropertyChanged += OnMainPropertyChanged;
            Main.Sites.PropertyChanged += OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged += OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged += OnRelatedPropertyChanged;
            Window.Closed += OnClosed;
        }

        public void OnToggleClick(object sender, RoutedEventArgs e)
        {
            _expanded = !_expanded;
            Host.ColumnDefinitions[1].Width = new GridLength(_expanded ? 300 : 44);
            HeaderText.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            DetailPanel.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            foreach (var label in Labels)
                label.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        public async void OnNavigationClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string page) return;
            try
            {
                if (Main.NavigateCommand.CanExecute(page))
                    await Main.NavigateCommand.ExecuteAsync(page);
            }
            catch { }
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(MainWindowViewModel.CurrentPage) or
                nameof(MainWindowViewModel.DashboardSelectedSite) or
                nameof(MainWindowViewModel.DashboardRunningJobs) or
                nameof(MainWindowViewModel.DashboardFailedJobs))
                Refresh();
        }

        private void OnRelatedPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

        public void Refresh()
        {
            if (!_expanded) return;
            PageText.Text = string.IsNullOrWhiteSpace(Main.CurrentPage) ? "Dashboard" : Main.CurrentPage;
            SiteText.Text = string.IsNullOrWhiteSpace(Main.DashboardSelectedSite)
                ? "No active website selected"
                : $"Active website: {Main.DashboardSelectedSite}";
            StatusText.Text =
                $"Pending: {Main.SuggestedChanges.PendingCount}\nApproved: {Main.SuggestedChanges.ApprovedCount}\nReady: {Main.ExecutionCenter.ReadyCount}\nRunning: {Main.DashboardRunningJobs}\nErrors: {Main.DashboardFailedJobs}";
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Main.PropertyChanged -= OnMainPropertyChanged;
            Main.Sites.PropertyChanged -= OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged -= OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged -= OnRelatedPropertyChanged;
            Window.Closed -= OnClosed;

            foreach (var button in NavigationButtons)
                button.Click -= OnNavigationClick;

            if (Host.Parent is Panel parent) parent.Children.Remove(Host);
            Sidebar.Child = null;
            NavigationButtons.Clear();
            Labels.Clear();
        }
    }
}
