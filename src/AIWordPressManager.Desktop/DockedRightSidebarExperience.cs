using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

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

        state.ToggleButton = new Button
        {
            Content = "☰",
            Width = 32,
            Height = 30,
            Padding = new Thickness(0),
            ToolTip = "Open or close workspace sidebar",
            Focusable = false
        };
        state.ToggleButton.Click += state.OnToggleClick;
        Grid.SetColumn(state.ToggleButton, 1);
        header.Children.Add(state.ToggleButton);
        layout.Children.Add(header);

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

        state.InitializeViews();
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
        private bool _refreshQueued;
        private bool _notificationsDirty = true;
        private SidebarSection _section = SidebarSection.Journey;
        private readonly Dictionary<SidebarSection, UIElement> _views = [];

        private TextBlock _journeyTitle = null!;
        private TextBlock _journeyDescription = null!;
        private ProgressBar _journeyProgress = null!;
        private TextBlock _journeyPercent = null!;

        private TextBlock _aiTitle = null!;
        private TextBlock _aiDescription = null!;
        private TextBlock _aiPending = null!;
        private TextBlock _aiApproved = null!;

        private TextBlock _operationTitle = null!;
        private TextBlock _operationStep = null!;
        private ProgressBar _operationProgress = null!;
        private TextBlock _operationDetail = null!;

        private StackPanel _notificationItems = null!;

        private TextBlock _quickTitle = null!;
        private TextBlock _quickApproved = null!;
        private TextBlock _quickReady = null!;
        private TextBlock _quickFailed = null!;

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

        public void InitializeViews()
        {
            _views[SidebarSection.Journey] = BuildJourneyView();
            _views[SidebarSection.Ai] = BuildAiView();
            _views[SidebarSection.Operations] = BuildOperationsView();
            _views[SidebarSection.Notifications] = BuildNotificationsView();
            _views[SidebarSection.QuickFix] = BuildQuickFixView();
        }

        public void Attach()
        {
            Main.PropertyChanged += OnMainPropertyChanged;
            Main.Sites.PropertyChanged += OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged += OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged += OnRelatedPropertyChanged;
            Main.SuggestedChanges.Items.CollectionChanged += OnCollectionChanged;
            Main.ExecutionCenter.Items.CollectionChanged += OnCollectionChanged;
            Main.Operations.History.CollectionChanged += OnHistoryChanged;
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
            QueueRefresh();
        }

        public void OnSectionClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SidebarSection section) return;
            _section = section;
            if (!_expanded)
                OnToggleClick(sender, e);
            else
                QueueRefresh();
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
                nameof(MainWindowViewModel.DashboardAiSuggestions) or
                nameof(MainWindowViewModel.IsOperationRunning) or
                nameof(MainWindowViewModel.OperationProgress) or
                nameof(MainWindowViewModel.OperationTitle) or
                nameof(MainWindowViewModel.OperationStep) or
                nameof(MainWindowViewModel.OperationDetail))
                QueueRefresh();
        }

        private void OnRelatedPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

        private void OnHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _notificationsDirty = true;
            QueueRefresh();
        }

        private void QueueRefresh()
        {
            if (!_expanded || _refreshQueued || Window.Dispatcher.HasShutdownStarted) return;
            _refreshQueued = true;
            Window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                RefreshVisibleSection();
            }));
        }

        private void RefreshVisibleSection()
        {
            if (!_expanded || !Window.IsLoaded) return;

            HeaderText.Text = SectionTitle(_section);
            foreach (var button in SectionButtons)
                button.FontWeight = Equals(button.Tag, _section) ? FontWeights.Bold : FontWeights.Normal;

            var view = _views[_section];
            if (DetailHost.Children.Count != 1 || !ReferenceEquals(DetailHost.Children[0], view))
            {
                DetailHost.Children.Clear();
                DetailHost.Children.Add(view);
            }

            switch (_section)
            {
                case SidebarSection.Journey:
                    RefreshJourney();
                    break;
                case SidebarSection.Ai:
                    RefreshAi();
                    break;
                case SidebarSection.Operations:
                    RefreshOperations();
                    break;
                case SidebarSection.Notifications:
                    RefreshNotifications();
                    break;
                case SidebarSection.QuickFix:
                    RefreshQuickFix();
                    break;
            }
        }

        private UIElement BuildJourneyView()
        {
            var panel = BasePanel("CURRENT JOURNEY");
            _journeyTitle = Title(null);
            _journeyDescription = Body(null);
            _journeyProgress = Progress();
            _journeyPercent = Body(null);
            panel.Children.Add(_journeyTitle);
            panel.Children.Add(_journeyDescription);
            panel.Children.Add(_journeyProgress);
            panel.Children.Add(_journeyPercent);
            panel.Children.Add(OpenButton("Open dashboard journey", "Dashboard"));
            return Scroll(panel);
        }

        private UIElement BuildAiView()
        {
            var panel = BasePanel("AI COPILOT");
            _aiTitle = Title(null);
            _aiDescription = Body(null);
            _aiPending = MetricText("Pending", 0);
            _aiApproved = MetricText("Approved", 0);
            panel.Children.Add(_aiTitle);
            panel.Children.Add(_aiDescription);
            panel.Children.Add(Metric(_aiPending));
            panel.Children.Add(Metric(_aiApproved));
            panel.Children.Add(OpenButton("Open AI Studio", "AI Studio"));
            panel.Children.Add(OpenButton("Review suggested changes", "Suggested Changes"));
            return Scroll(panel);
        }

        private UIElement BuildOperationsView()
        {
            var panel = BasePanel("LIVE OPERATIONS");
            _operationTitle = Title(null);
            _operationStep = Body(null);
            _operationProgress = Progress();
            _operationDetail = Body(null);
            panel.Children.Add(_operationTitle);
            panel.Children.Add(_operationStep);
            panel.Children.Add(_operationProgress);
            panel.Children.Add(_operationDetail);
            panel.Children.Add(OpenButton("Open Operations Center", "Operations Center"));
            panel.Children.Add(OpenButton("Open Jobs", "Jobs"));
            return Scroll(panel);
        }

        private UIElement BuildNotificationsView()
        {
            var panel = BasePanel("LATEST EVENTS");
            _notificationItems = new StackPanel();
            panel.Children.Add(_notificationItems);
            panel.Children.Add(OpenButton("Open Notification Center", "Notification Center"));
            panel.Children.Add(OpenButton("Open Activity Timeline", "Activity Timeline"));
            return Scroll(panel);
        }

        private UIElement BuildQuickFixView()
        {
            var panel = BasePanel("QUICK FIX QUEUE");
            _quickTitle = Title(null);
            _quickApproved = MetricText("Approved", 0);
            _quickReady = MetricText("Ready", 0);
            _quickFailed = MetricText("Failed", 0);
            panel.Children.Add(_quickTitle);
            panel.Children.Add(Metric(_quickApproved));
            panel.Children.Add(Metric(_quickReady));
            panel.Children.Add(Metric(_quickFailed));
            panel.Children.Add(OpenButton("Review changes", "Suggested Changes"));
            panel.Children.Add(OpenButton("Open Execution Center", "Execution Center"));
            return Scroll(panel);
        }

        private void RefreshJourney()
        {
            _journeyTitle.Text = string.IsNullOrWhiteSpace(Main.CurrentJourneyStepTitle)
                ? "Select or connect a website"
                : Main.CurrentJourneyStepTitle;
            _journeyDescription.Text = string.IsNullOrWhiteSpace(Main.CurrentJourneyStepDescription)
                ? "No details available."
                : Main.CurrentJourneyStepDescription;
            var value = Math.Clamp(Main.DashboardJourneyProgress, 0, 100);
            _journeyProgress.Value = value;
            _journeyPercent.Text = $"Journey progress: {value}%";
        }

        private void RefreshAi()
        {
            _aiTitle.Text = Main.DashboardAiSuggestions > 0
                ? $"{Main.DashboardAiSuggestions} AI suggestion(s) available"
                : "No AI suggestions yet";
            _aiDescription.Text = Main.DashboardAiSuggestions > 0
                ? "Review the existing AI recommendations, risk and expected result before approval."
                : "Synchronize and analyze the website to generate reviewable recommendations.";
            _aiPending.Text = $"Pending: {Main.SuggestedChanges.PendingCount}";
            _aiApproved.Text = $"Approved: {Main.SuggestedChanges.ApprovedCount}";
        }

        private void RefreshOperations()
        {
            var running = Main.IsOperationRunning || Main.IsGuidedAnalysisRunning || Main.IsSafeAutopilotRunning;
            _operationTitle.Text = running ? "Operation running" : "No operation is running";
            _operationStep.Text = string.IsNullOrWhiteSpace(Main.OperationStep) ? "Idle" : Main.OperationStep;
            _operationProgress.Value = Math.Clamp(Main.OperationProgress, 0, 100);
            _operationDetail.Text = string.IsNullOrWhiteSpace(Main.OperationDetail)
                ? $"Running jobs: {Main.DashboardRunningJobs}"
                : Main.OperationDetail;
        }

        private void RefreshNotifications()
        {
            if (!_notificationsDirty) return;
            _notificationsDirty = false;
            _notificationItems.Children.Clear();

            var history = Main.Operations.History.Take(8).ToArray();
            if (history.Length == 0)
            {
                _notificationItems.Children.Add(Body("No workflow events yet."));
                return;
            }

            foreach (var item in history)
            {
                _notificationItems.Children.Add(new Border
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

        private void RefreshQuickFix()
        {
            _quickTitle.Text = Main.SuggestedChanges.PendingCount > 0
                ? $"{Main.SuggestedChanges.PendingCount} change(s) need review"
                : "No pending changes";
            _quickApproved.Text = $"Approved: {Main.SuggestedChanges.ApprovedCount}";
            _quickReady.Text = $"Ready: {Main.ExecutionCenter.ReadyCount}";
            _quickFailed.Text = $"Failed: {Main.ExecutionCenter.FailedCount}";
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

        private static ProgressBar Progress() => new()
        {
            Minimum = 0,
            Maximum = 100,
            Height = 8,
            Margin = new Thickness(0, 12, 0, 5)
        };

        private static TextBlock MetricText(string label, int value) => new()
        {
            Text = $"{label}: {value}",
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        };

        private static Border Metric(TextBlock text) => new()
        {
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(6),
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            Child = text
        };

        private static ScrollViewer Scroll(UIElement content) => new()
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        private static string SectionTitle(SidebarSection section) => section switch
        {
            SidebarSection.Journey => "Journey",
            SidebarSection.Ai => "AI Copilot",
            SidebarSection.Operations => "Operations",
            SidebarSection.Notifications => "Notifications",
            SidebarSection.QuickFix => "Quick Fix",
            _ => "Workspace"
        };

        private void OnClosed(object? sender, EventArgs e)
        {
            Main.PropertyChanged -= OnMainPropertyChanged;
            Main.Sites.PropertyChanged -= OnRelatedPropertyChanged;
            Main.SuggestedChanges.PropertyChanged -= OnRelatedPropertyChanged;
            Main.ExecutionCenter.PropertyChanged -= OnRelatedPropertyChanged;
            Main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            Main.ExecutionCenter.Items.CollectionChanged -= OnCollectionChanged;
            Main.Operations.History.CollectionChanged -= OnHistoryChanged;
            Main.Operations.Operations.CollectionChanged -= OnCollectionChanged;
            Window.Closed -= OnClosed;

            ToggleButton.Click -= OnToggleClick;
            foreach (var button in SectionButtons)
                button.Click -= OnSectionClick;

            DetailHost.Children.Clear();
            _views.Clear();
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
