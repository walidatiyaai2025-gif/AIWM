using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Converts the main work area into content plus one docked right workspace.
/// Existing journey, operation, notification, AI and suggestion data is surfaced
/// in-place; full pages remain available through the existing NavigateCommand.
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
        state.Sidebar = BuildSidebar(state);
        Grid.SetColumn(state.Sidebar, 1);
        host.Children.Add(state.Sidebar);
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
        state.ToggleButton = toggle;

        var tabs = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(5, 2, 5, 6)
        };
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);

        tabs.Children.Add(TabButton("✓", "Journey", SidebarSection.Journey, state));
        tabs.Children.Add(TabButton("✦", "AI Copilot", SidebarSection.Ai, state));
        tabs.Children.Add(TabButton("▶", "Operations", SidebarSection.Operations, state));
        tabs.Children.Add(TabButton("🔔", "Notifications", SidebarSection.Notifications, state));
        tabs.Children.Add(TabButton("⚡", "Quick Fix", SidebarSection.QuickFix, state));

        state.DetailHost = new Grid
        {
            Margin = new Thickness(12, 8, 12, 12),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(state.DetailHost, 2);
        layout.Children.Add(state.DetailHost);

        return shell;
    }

    private static Button TabButton(string icon, string label, SidebarSection section, State state)
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
            Tag = section
        };
        button.Click += state.OnSectionClick;
        state.SectionButtons.Add(button);
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
        private SidebarSection _section = SidebarSection.Journey;

        public MainWindow Window { get; } = window;
        public Grid Root { get; } = root;
        public MainWindowViewModel Main { get; } = main;
        public Grid Host { get; } = host;
        public Grid ContentHost { get; } = contentHost;
        public Border Sidebar { get; set; } = null!;
        public Button ToggleButton { get; set; } = null!;
        public TextBlock HeaderText { get; set; } = null!;
        public Grid DetailHost { get; set; } = null!;
        public List<Button> SectionButtons { get; } = [];
        public List<TextBlock> Labels { get; } = [];

        public void Attach()
        {
            Main.PropertyChanged += OnMainPropertyChanged;
            Main.Sites.PropertyChanged += OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged += OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged += OnRelatedPropertyChanged;
            Main.SuggestedChanges.Items.CollectionChanged += OnCollectionChanged;
            Main.ExecutionCenter.Items.CollectionChanged += OnCollectionChanged;
            Main.Operations.History.CollectionChanged += OnCollectionChanged;
            Main.Operations.Operations.CollectionChanged += OnCollectionChanged;
            Window.Closed += OnClosed;
        }

        public void OnToggleClick(object sender, RoutedEventArgs e)
        {
            _expanded = !_expanded;
            Host.ColumnDefinitions[1].Width = new GridLength(_expanded ? 320 : 44);
            HeaderText.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            DetailHost.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            ToggleButton.Content = _expanded ? "✕" : "☰";
            ToggleButton.ToolTip = _expanded ? "Close workspace sidebar" : "Open workspace sidebar";
            foreach (var label in Labels)
                label.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        }

        public void OnSectionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SidebarSection section) return;
            _section = section;
            if (!_expanded)
                OnToggleClick(sender, e);
            else
                Refresh();
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(MainWindowViewModel.CurrentPage) or
                nameof(MainWindowViewModel.DashboardSelectedSite) or
                nameof(MainWindowViewModel.DashboardRunningJobs) or
                nameof(MainWindowViewModel.DashboardFailedJobs) or
                nameof(MainWindowViewModel.CurrentJourneyStepTitle) or
                nameof(MainWindowViewModel.CurrentJourneyStepDescription) or
                nameof(MainWindowViewModel.DashboardJourneyProgress) or
                nameof(MainWindowViewModel.IsOperationRunning) or
                nameof(MainWindowViewModel.OperationProgress) or
                nameof(MainWindowViewModel.OperationTitle) or
                nameof(MainWindowViewModel.OperationStep) or
                nameof(MainWindowViewModel.OperationDetail))
                Refresh();
        }

        private void OnRelatedPropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

        public void Refresh()
        {
            if (!_expanded || !Window.IsLoaded) return;

            HeaderText.Text = _section switch
            {
                SidebarSection.Journey => "Journey",
                SidebarSection.Ai => "AI Copilot",
                SidebarSection.Operations => "Operations",
                SidebarSection.Notifications => "Notifications",
                SidebarSection.QuickFix => "Quick Fix",
                _ => "Workspace"
            };

            foreach (var button in SectionButtons)
                button.FontWeight = Equals(button.Tag, _section) ? FontWeights.Bold : FontWeights.Normal;

            DetailHost.Children.Clear();
            DetailHost.Children.Add(_section switch
            {
                SidebarSection.Journey => BuildJourneyContent(),
                SidebarSection.Ai => BuildAiContent(),
                SidebarSection.Operations => BuildOperationsContent(),
                SidebarSection.Notifications => BuildNotificationsContent(),
                SidebarSection.QuickFix => BuildQuickFixContent(),
                _ => new TextBlock { Text = "No workspace selected." }
            });
        }

        private UIElement BuildJourneyContent()
        {
            var panel = BasePanel("CURRENT JOURNEY");
            panel.Children.Add(Title(string.IsNullOrWhiteSpace(Main.CurrentJourneyStepTitle)
                ? "Select or connect a website"
                : Main.CurrentJourneyStepTitle));
            panel.Children.Add(Body(Main.CurrentJourneyStepDescription));
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(Main.DashboardJourneyProgress, 0, 100),
                Height = 8,
                Margin = new Thickness(0, 12, 0, 5)
            });
            panel.Children.Add(Body($"Journey progress: {Math.Clamp(Main.DashboardJourneyProgress, 0, 100)}%"));
            panel.Children.Add(OpenButton("Open dashboard journey", "Dashboard"));
            return Scroll(panel);
        }

        private UIElement BuildAiContent()
        {
            var panel = BasePanel("AI COPILOT");
            panel.Children.Add(Title(Main.DashboardAiSuggestions > 0
                ? $"{Main.DashboardAiSuggestions} AI suggestion(s) available"
                : "No AI suggestions yet"));
            panel.Children.Add(Body(Main.DashboardAiSuggestions > 0
                ? "Review the existing AI recommendations, risk and expected result before approval."
                : "Synchronize and analyze the website to generate reviewable recommendations."));
            panel.Children.Add(Metric("Pending", Main.SuggestedChanges.PendingCount));
            panel.Children.Add(Metric("Approved", Main.SuggestedChanges.ApprovedCount));
            panel.Children.Add(OpenButton("Open AI Studio", "AI Studio"));
            panel.Children.Add(OpenButton("Review suggested changes", "Suggested Changes"));
            return Scroll(panel);
        }

        private UIElement BuildOperationsContent()
        {
            var panel = BasePanel("LIVE OPERATIONS");
            var running = Main.IsOperationRunning || Main.IsGuidedAnalysisRunning || Main.IsSafeAutopilotRunning;
            panel.Children.Add(Title(running ? "Operation running" : "No operation is running"));
            panel.Children.Add(Body(string.IsNullOrWhiteSpace(Main.OperationTitle) ? "Ready" : Main.OperationTitle));
            panel.Children.Add(Body(string.IsNullOrWhiteSpace(Main.OperationStep) ? "Idle" : Main.OperationStep));
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = Math.Clamp(Main.OperationProgress, 0, 100),
                Height = 8,
                Margin = new Thickness(0, 12, 0, 5)
            });
            panel.Children.Add(Body(string.IsNullOrWhiteSpace(Main.OperationDetail)
                ? $"Running jobs: {Main.DashboardRunningJobs}"
                : Main.OperationDetail));
            panel.Children.Add(OpenButton("Open Operations Center", "Operations Center"));
            panel.Children.Add(OpenButton("Open Jobs", "Jobs"));
            return Scroll(panel);
        }

        private UIElement BuildNotificationsContent()
        {
            var panel = BasePanel("LATEST EVENTS");
            var history = Main.Operations.History.Take(8).ToArray();
            if (history.Length == 0)
            {
                panel.Children.Add(Body("No workflow events yet."));
            }
            else
            {
                foreach (var item in history)
                {
                    panel.Children.Add(new Border
                    {
                        Margin = new Thickness(0, 0, 0, 7),
                        Padding = new Thickness(8),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brush("BorderBrush", Brushes.LightGray),
                        Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
                        Child = new TextBlock
                        {
                            Text = $"{item.State} • {item.Step}\n{item.Detail}",
                            FontSize = 10.5,
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
                        }
                    });
                }
            }
            panel.Children.Add(OpenButton("Open Notification Center", "Notification Center"));
            panel.Children.Add(OpenButton("Open Activity Timeline", "Activity Timeline"));
            return Scroll(panel);
        }

        private UIElement BuildQuickFixContent()
        {
            var panel = BasePanel("QUICK FIX QUEUE");
            panel.Children.Add(Title(Main.SuggestedChanges.PendingCount > 0
                ? $"{Main.SuggestedChanges.PendingCount} change(s) need review"
                : "No pending changes"));
            panel.Children.Add(Metric("Approved", Main.SuggestedChanges.ApprovedCount));
            panel.Children.Add(Metric("Ready", Main.ExecutionCenter.ReadyCount));
            panel.Children.Add(Metric("Failed", Main.ExecutionCenter.FailedCount));
            panel.Children.Add(OpenButton("Review changes", "Suggested Changes"));
            panel.Children.Add(OpenButton("Open Execution Center", "Execution Center"));
            return Scroll(panel);
        }

        private Button OpenButton(string label, string page)
        {
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 6, 10, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += async (_, _) =>
            {
                if (Main.NavigateCommand.CanExecute(page))
                    await Main.NavigateCommand.ExecuteAsync(page);
            };
            return button;
        }

        private static StackPanel BasePanel(string caption)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("PrimaryBrush", Brushes.Teal),
                Margin = new Thickness(0, 0, 0, 7)
            });
            return panel;
        }

        private static TextBlock Title(string? value) => new()
        {
            Text = string.IsNullOrWhiteSpace(value) ? "Not available" : value,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black),
            Margin = new Thickness(0, 0, 0, 6)
        };

        private static TextBlock Body(string? value) => new()
        {
            Text = string.IsNullOrWhiteSpace(value) ? "No details available." : value,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray),
            Margin = new Thickness(0, 0, 0, 5)
        };

        private static Border Metric(string label, int value) => new()
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(6),
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = new TextBlock
            {
                Text = $"{label}: {value}",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextPrimaryBrush", Brushes.Black)
            }
        };

        private static ScrollViewer Scroll(UIElement content) => new()
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        private void OnClosed(object? sender, EventArgs e)
        {
            Main.PropertyChanged -= OnMainPropertyChanged;
            Main.Sites.PropertyChanged -= OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged -= OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged -= OnRelatedPropertyChanged;
            Main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            Main.ExecutionCenter.Items.CollectionChanged -= OnCollectionChanged;
            Main.Operations.History.CollectionChanged -= OnCollectionChanged;
            Main.Operations.Operations.CollectionChanged -= OnCollectionChanged;
            Window.Closed -= OnClosed;

            ToggleButton.Click -= OnToggleClick;
            foreach (var button in SectionButtons)
                button.Click -= OnSectionClick;

            DetailHost.Children.Clear();
            if (Host.Parent is Panel parent) parent.Children.Remove(Host);
            Sidebar.Child = null;
            SectionButtons.Clear();
            Labels.Clear();
        }
    }

    private enum SidebarSection
    {
        Journey,
        Ai,
        Operations,
        Notifications,
        QuickFix
    }
}
