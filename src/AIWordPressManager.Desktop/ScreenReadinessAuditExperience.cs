using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ScreenReadinessAuditExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly ScreenDefinition[] Screens =
    [
        new("Sites", nameof(MainWindowViewModel.Sites), ["LoadCommand", "RefreshCommand", "AddSiteCommand"]),
        new("WordPress Explorer", nameof(MainWindowViewModel.Explorer), ["RefreshCommand", "ClearFiltersCommand", "OpenSelectedContentCommand", "OpenSelectedMediaCommand"]),
        new("SEO Audit", nameof(MainWindowViewModel.SeoAudit), ["RunAuditCommand", "OpenSelectedCommand", "CopySelectedLinkCommand"]),
        new("Suggested Changes", nameof(MainWindowViewModel.SuggestedChanges), ["GenerateCommand", "RefreshCommand", "ApproveCommand", "RejectCommand", "ApplySafeSelectedCommand"]),
        new("Execution Center", nameof(MainWindowViewModel.ExecutionCenter), ["LoadCommand", "RefreshCommand", "ExecuteAllReadyCommand", "RetryFailedCommand"]),
        new("Backups", nameof(MainWindowViewModel.Backups), ["LoadCommand", "RefreshCommand", "CreateBackupCommand", "RestoreCommand"]),
        new("Health Center", nameof(MainWindowViewModel.HealthCenter), ["LoadCommand", "RefreshCommand"]),
        new("Transaction Center", nameof(MainWindowViewModel.TransactionCenter), ["LoadCommand", "ReconcileCommand", "ExportCsvCommand"]),
        new("Activity Timeline", null, []),
        new("Evidence Center", nameof(MainWindowViewModel.EvidenceCenter), ["LoadCommand", "RefreshCommand"])
    ];

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

        Attached.Add(window, new object());
        var host = FindTopBar(root);
        if (host is null) return;

        var button = new Button
        {
            Content = "✓ Screen Check",
            ToolTip = "Audit navigation and screen command readiness without writing to WordPress",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26
        };
        button.Click += async (_, _) => await ShowAuditAsync(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), button);

        window.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key != Key.F9 ||
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;
            args.Handled = true;
            await ShowAuditAsync(window, main);
        };
    }

    private static async Task ShowAuditAsync(Window owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Screen readiness audit",
            Width = 980,
            Height = 720,
            MinWidth = 700,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = Brush("SurfaceBrush", Brushes.White)
        };

        var layout = new DockPanel { Margin = new Thickness(18) };
        dialog.Content = layout;

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var resultsHost = new StackPanel();
        var viewer = new ScrollViewer
        {
            Content = resultsHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        layout.Children.Add(viewer);

        var run = ActionButton("Run navigation smoke test");
        var copy = ActionButton("Copy report");
        var close = ActionButton("Close");
        footer.Children.Add(run);
        footer.Children.Add(copy);
        footer.Children.Add(close);

        AuditReport report = BuildStaticAudit(main);
        Render(resultsHost, report);

        run.Click += async (_, _) =>
        {
            run.IsEnabled = false;
            try
            {
                report = await RunNavigationAuditAsync(main);
                Render(resultsHost, report);
            }
            finally
            {
                run.IsEnabled = true;
            }
        };
        copy.Click += (_, _) => Clipboard.SetText(report.ToText());
        close.Click += (_, _) => dialog.Close();

        dialog.ShowDialog();
        await Task.CompletedTask;
    }

    private static AuditReport BuildStaticAudit(MainWindowViewModel main)
    {
        var hasSite = main.Sites.SelectedSite is not null;
        var results = Screens.Select(screen => InspectScreen(main, screen, hasSite)).ToList();
        return new AuditReport(DateTime.Now, hasSite, false, results);
    }

    private static async Task<AuditReport> RunNavigationAuditAsync(MainWindowViewModel main)
    {
        var original = main.CurrentPage;
        var hasSite = main.Sites.SelectedSite is not null;
        var results = new List<ScreenAuditResult>();

        try
        {
            foreach (var screen in Screens)
            {
                var result = InspectScreen(main, screen, hasSite);
                try
                {
                    if (main.NavigateCommand.CanExecute(screen.Page))
                    {
                        await main.NavigateCommand.ExecuteAsync(screen.Page);
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                            () => { },
                            System.Windows.Threading.DispatcherPriority.ContextIdle);
                        result = result with
                        {
                            NavigationState = string.Equals(main.CurrentPage, screen.Page, StringComparison.OrdinalIgnoreCase)
                                ? Readiness.Ready
                                : Readiness.Missing,
                            NavigationDetail = string.Equals(main.CurrentPage, screen.Page, StringComparison.OrdinalIgnoreCase)
                                ? "Navigation reached the expected page."
                                : $"Expected '{screen.Page}', current page is '{main.CurrentPage}'."
                        };
                    }
                    else
                    {
                        result = result with
                        {
                            NavigationState = Readiness.Blocked,
                            NavigationDetail = "NavigateCommand is currently disabled."
                        };
                    }
                }
                catch (Exception ex)
                {
                    result = result with
                    {
                        NavigationState = Readiness.Missing,
                        NavigationDetail = $"Navigation threw {ex.GetType().Name}: {ex.Message}"
                    };
                }
                results.Add(result);
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(original) && main.NavigateCommand.CanExecute(original))
                await main.NavigateCommand.ExecuteAsync(original);
        }

        return new AuditReport(DateTime.Now, hasSite, true, results);
    }

    private static ScreenAuditResult InspectScreen(MainWindowViewModel main, ScreenDefinition screen, bool hasSite)
    {
        object? viewModel = null;
        var vmState = Readiness.Ready;
        var vmDetail = "Screen is hosted directly by MainWindow.";

        if (!string.IsNullOrWhiteSpace(screen.ViewModelProperty))
        {
            var property = typeof(MainWindowViewModel).GetProperty(screen.ViewModelProperty,
                BindingFlags.Instance | BindingFlags.Public);
            viewModel = property?.GetValue(main);
            vmState = property is null || viewModel is null ? Readiness.Missing : Readiness.Ready;
            vmDetail = property is null
                ? $"Property '{screen.ViewModelProperty}' was not found."
                : viewModel is null
                    ? $"Property '{screen.ViewModelProperty}' is null."
                    : $"{viewModel.GetType().Name} is available.";
        }

        var commands = new List<CommandAuditResult>();
        if (viewModel is not null)
        {
            foreach (var commandName in screen.Commands)
            {
                var property = viewModel.GetType().GetProperty(commandName,
                    BindingFlags.Instance | BindingFlags.Public);
                var value = property?.GetValue(viewModel);
                if (value is not ICommand command)
                {
                    commands.Add(new CommandAuditResult(commandName, Readiness.Missing,
                        property is null ? "Command property is missing." : "Property does not implement ICommand."));
                    continue;
                }

                var canExecute = false;
                try { canExecute = command.CanExecute(null); }
                catch (Exception ex)
                {
                    commands.Add(new CommandAuditResult(commandName, Readiness.Missing,
                        $"CanExecute threw {ex.GetType().Name}: {ex.Message}"));
                    continue;
                }

                commands.Add(new CommandAuditResult(
                    commandName,
                    canExecute ? Readiness.Ready : Readiness.Blocked,
                    canExecute
                        ? "Command is available and enabled."
                        : hasSite
                            ? "Command exists but current selection/data requirements are not satisfied."
                            : "Command exists; select a website before testing site-dependent actions."));
            }
        }

        return new ScreenAuditResult(screen.Page, vmState, vmDetail,
            Readiness.NotTested, "Run the navigation smoke test.", commands);
    }

    private static void Render(Panel host, AuditReport report)
    {
        host.Children.Clear();
        host.Children.Add(new TextBlock
        {
            Text = "Screen readiness audit",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        host.Children.Add(new TextBlock
        {
            Text = $"Checked {report.Results.Count} screens • Site selected: {(report.HasSelectedSite ? "Yes" : "No")} • Navigation tested: {(report.NavigationWasTested ? "Yes" : "No")}",
            Margin = new Thickness(0, 4, 0, 14),
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        foreach (var screen in report.Results)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("BorderBrush", Brushes.LightGray),
                Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
                CornerRadius = new CornerRadius(7)
            };
            var stack = new StackPanel();
            card.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = $"{Symbol(screen.Overall)} {screen.Page} — {screen.Overall}",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = StateBrush(screen.Overall)
            });
            stack.Children.Add(Detail("ViewModel", screen.ViewModelState, screen.ViewModelDetail));
            stack.Children.Add(Detail("Navigation", screen.NavigationState, screen.NavigationDetail));
            foreach (var command in screen.Commands)
                stack.Children.Add(Detail(command.Name, command.State, command.Detail));
            host.Children.Add(card);
        }
    }

    private static TextBlock Detail(string label, Readiness state, string detail) => new()
    {
        Text = $"  {Symbol(state)} {label}: {detail}",
        Margin = new Thickness(0, 3, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = StateBrush(state)
    };

    private static string Symbol(Readiness state) => state switch
    {
        Readiness.Ready => "✓",
        Readiness.Blocked => "○",
        Readiness.Missing => "✕",
        _ => "•"
    };

    private static Brush StateBrush(Readiness state) => state switch
    {
        Readiness.Ready => Brushes.SeaGreen,
        Readiness.Blocked => Brushes.DarkGoldenrod,
        Readiness.Missing => Brushes.IndianRed,
        _ => Brush("TextSecondaryBrush", Brushes.DimGray)
    };

    private static Button ActionButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(7, 0, 0, 0),
        Padding = new Thickness(12, 7, 12, 7),
        MinWidth = 110
    };

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            var texts = panel.Children.OfType<FrameworkElement>()
                .SelectMany(Enumerate<TextBlock>)
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            if (texts.Any(x => x.Contains("Active:", StringComparison.OrdinalIgnoreCase))) return panel;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record ScreenDefinition(string Page, string? ViewModelProperty, string[] Commands);
    private sealed record CommandAuditResult(string Name, Readiness State, string Detail);
    private sealed record ScreenAuditResult(
        string Page,
        Readiness ViewModelState,
        string ViewModelDetail,
        Readiness NavigationState,
        string NavigationDetail,
        IReadOnlyList<CommandAuditResult> Commands)
    {
        public Readiness Overall => ViewModelState == Readiness.Missing || NavigationState == Readiness.Missing || Commands.Any(x => x.State == Readiness.Missing)
            ? Readiness.Missing
            : ViewModelState == Readiness.Blocked || NavigationState == Readiness.Blocked || Commands.Any(x => x.State == Readiness.Blocked)
                ? Readiness.Blocked
                : NavigationState == Readiness.NotTested ? Readiness.NotTested : Readiness.Ready;
    }

    private sealed record AuditReport(DateTime CheckedAt, bool HasSelectedSite, bool NavigationWasTested, IReadOnlyList<ScreenAuditResult> Results)
    {
        public string ToText()
        {
            var builder = new StringBuilder()
                .AppendLine("AI WORDPRESS MANAGER — SCREEN READINESS AUDIT")
                .AppendLine($"Checked: {CheckedAt:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Site selected: {HasSelectedSite}")
                .AppendLine($"Navigation tested: {NavigationWasTested}")
                .AppendLine();
            foreach (var screen in Results)
            {
                builder.AppendLine($"[{screen.Overall}] {screen.Page}");
                builder.AppendLine($"  ViewModel: {screen.ViewModelState} — {screen.ViewModelDetail}");
                builder.AppendLine($"  Navigation: {screen.NavigationState} — {screen.NavigationDetail}");
                foreach (var command in screen.Commands)
                    builder.AppendLine($"  {command.Name}: {command.State} — {command.Detail}");
                builder.AppendLine();
            }
            return builder.ToString();
        }
    }

    private enum Readiness { NotTested, Ready, Blocked, Missing }
}
