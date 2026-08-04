using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ContextualScreenActionsExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly IReadOnlyDictionary<string, ScreenDefinition> Registry =
        new Dictionary<string, ScreenDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sites"] = new(nameof(MainWindowViewModel.Sites),
                A("Refresh sites", "LoadCommand", "RefreshCommand"),
                A("Add website", "AddSiteCommand", "AddCommand"),
                A("Test selected connection", "RetestSelectedSiteCommand", "TestSelectedCommand"),
                A("Test all connections", "RetestAllSitesCommand"),
                A("Open website", "OpenSelectedSiteCommand"),
                A("Open WordPress admin", "OpenWordPressAdminCommand"),
                A("Copy website URL", "CopySelectedUrlCommand"),
                A("Remove website", true, "DeleteSelectedSiteCommand", "DeleteSelectedCommand")),

            ["WordPress Explorer"] = new(nameof(MainWindowViewModel.Explorer),
                A("Synchronize now", "RefreshCommand"),
                A("Clear filters", "ClearFiltersCommand"),
                A("Open selected content", "OpenSelectedContentCommand"),
                A("Copy selected content link", "CopySelectedContentLinkCommand"),
                A("Open selected media", "OpenSelectedMediaCommand"),
                A("Copy selected media URL", "CopySelectedMediaUrlCommand")),

            ["SEO Audit"] = new(nameof(MainWindowViewModel.SeoAudit),
                A("Run SEO audit", "RunAuditCommand", "RefreshCommand"),
                A("Open selected issue", "OpenSelectedCommand"),
                A("Copy selected link", "CopySelectedLinkCommand")),

            ["Suggested Changes"] = new(nameof(MainWindowViewModel.SuggestedChanges),
                A("Refresh proposals", "RefreshCommand", "LoadCommand"),
                A("Generate proposals", "GenerateCommand"),
                A("Show pending", "ShowPendingCommand"),
                A("Show approved", "ShowApprovedCommand"),
                A("Show rejected", "ShowRejectedCommand"),
                A("Approve selected", true, "BulkApproveCommand", "ApproveCommand"),
                A("Reject selected", true, "BulkRejectCommand", "RejectCommand"),
                A("Apply safe selected", true, "ApplySafeSelectedCommand")),

            ["Execution Center"] = new(nameof(MainWindowViewModel.ExecutionCenter),
                A("Refresh queue", "LoadCommand", "RefreshCommand"),
                A("Prepare supported", "PrepareAllSupportedCommand", "PrepareCommand"),
                A("Execute all ready", true, "ExecuteAllReadyCommand", "ExecuteReadyCommand"),
                A("Retry failed", true, "RetryFailedCommand")),

            ["Backups"] = new(nameof(MainWindowViewModel.Backups),
                A("Refresh backups", "LoadCommand", "RefreshCommand"),
                A("Create backup", "CreateBackupCommand"),
                A("Open backup folder", "OpenFolderCommand"),
                A("Import backup", "ImportBackupCommand"),
                A("Export selected", "ExportSelectedCommand"),
                A("Restore selected", true, "RestoreSelectedCommand", "RestoreCommand"),
                A("Restore from file", true, "RestoreFromFileCommand")),

            ["Health Center"] = new(nameof(MainWindowViewModel.HealthCenter),
                A("Refresh health", "LoadCommand", "RefreshCommand"),
                A("Run health checks", "RefreshCommand", "RunChecksCommand", "ScanCommand")),

            ["Transaction Center"] = new(nameof(MainWindowViewModel.TransactionCenter),
                A("Load transactions", "LoadCommand", "RefreshCommand"),
                A("Reconcile selected", true, "ReconcileCommand"),
                A("Export CSV", "ExportCsvCommand", "ExportCommand"),
                A("Copy selected JSON", "CopySelectedJsonCommand"),
                A("Open journal folder", "OpenJournalFolderCommand")),

            ["Evidence Center"] = new(nameof(MainWindowViewModel.EvidenceCenter),
                A("Load evidence", "LoadCommand", "RefreshCommand"),
                A("Refresh evidence", "RefreshCommand", "LoadCommand"),
                A("Open selected evidence", "OpenSelectedCommand", "OpenCommand"),
                A("Copy selected path", "CopySelectedPathCommand", "CopyPathCommand"))
        };

    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;
        Attached.Add(window, new object());

        var host = FindTopBar(root);
        if (host is null) return;

        var actions = HeaderButton("Actions ▾", "Commands for the current screen");
        actions.Click += (_, _) => OpenActions(actions, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), actions);

        var map = HeaderButton("Function Map", "Review registered functions and resolved command aliases");
        map.Click += (_, _) => ShowMap(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), map);

        window.PreviewKeyDown += (_, args) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || args.Key != Key.OemPeriod) return;
            args.Handled = true;
            OpenActions(actions, main);
        };
    }

    private static void OpenActions(Button owner, MainWindowViewModel main)
    {
        var menu = new ContextMenu();
        if (!Registry.TryGetValue(main.CurrentPage, out var definition))
        {
            menu.Items.Add(new MenuItem { Header = $"No quick actions for {main.CurrentPage}", IsEnabled = false });
        }
        else
        {
            var vm = ResolveViewModel(main, definition.ViewModelProperty);
            foreach (var action in definition.Actions)
            {
                var resolution = ResolveCommand(vm, action.CommandNames);
                var item = new MenuItem
                {
                    Header = action.Label,
                    IsEnabled = SafeCanExecute(resolution.Command),
                    ToolTip = Status(resolution, main.Sites.SelectedSite is not null),
                    Foreground = action.IsDestructive ? Brushes.IndianRed : null
                };
                item.Click += (_, _) =>
                {
                    try
                    {
                        if (resolution.Command is not null && resolution.Command.CanExecute(null))
                            resolution.Command.Execute(null);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(Window.GetWindow(owner), ex.ToString(), $"{action.Label} failed",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var openMap = new MenuItem { Header = "Open Function Map" };
        openMap.Click += (_, _) =>
        {
            if (Window.GetWindow(owner) is Window window) ShowMap(window, main);
        };
        menu.Items.Add(openMap);
        owner.ContextMenu = menu;
        menu.PlacementTarget = owner;
        menu.IsOpen = true;
    }

    private static void ShowMap(Window owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Screen function map",
            Width = 960,
            Height = 700,
            MinWidth = 700,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = Brush("SurfaceBrush", Brushes.White)
        };

        var dock = new DockPanel { Margin = new Thickness(18) };
        dialog.Content = dock;
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        var copy = ActionButton("Copy report");
        var close = ActionButton("Close");
        footer.Children.Add(copy);
        footer.Children.Add(close);

        var content = new StackPanel();
        dock.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(new TextBlock
        {
            Text = "Screen function map",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Commands are resolved through aliases. Ready means executable now; Blocked means selection or data is required; Missing means none of the registered command names exists.",
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        var report = new StringBuilder()
            .AppendLine("AI WORDPRESS MANAGER — SELF-HEALING FUNCTION MAP")
            .AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Website: {main.DashboardSelectedSite}")
            .AppendLine();

        foreach (var pair in Registry)
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
                Text = pair.Key,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = Brush("TextPrimaryBrush", Brushes.Black)
            });
            report.AppendLine($"[{pair.Key}]");

            var vm = ResolveViewModel(main, pair.Value.ViewModelProperty);
            foreach (var action in pair.Value.Actions)
            {
                var resolution = ResolveCommand(vm, action.CommandNames);
                var state = resolution.Command is null ? "Missing" : SafeCanExecute(resolution.Command) ? "Ready" : "Blocked";
                var detail = Status(resolution, main.Sites.SelectedSite is not null);
                stack.Children.Add(new TextBlock
                {
                    Text = $"{Symbol(state)} {action.Label} — {state} — {detail}",
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = StateBrush(state)
                });
                report.AppendLine($"  {action.Label}: {state} — {detail}");
            }
            report.AppendLine();
            content.Children.Add(card);
        }

        copy.Click += (_, _) => Clipboard.SetText(report.ToString());
        close.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    private static ScreenAction A(string label, params string[] commands) => new(label, false, commands);
    private static ScreenAction A(string label, bool destructive, params string[] commands) => new(label, destructive, commands);

    private static object? ResolveViewModel(MainWindowViewModel main, string property) =>
        typeof(MainWindowViewModel).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(main);

    private static CommandResolution ResolveCommand(object? vm, IReadOnlyList<string> names)
    {
        if (vm is null) return new(null, null, "ViewModel is unavailable.");
        foreach (var name in names)
        {
            var property = vm.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(vm) is ICommand command)
                return new(command, name, names.Count > 1 && name != names[0]
                    ? $"Resolved through alias '{name}'."
                    : $"Resolved as '{name}'.");
        }
        return new(null, null, $"None of these commands exists: {string.Join(", ", names)}.");
    }

    private static bool SafeCanExecute(ICommand? command)
    {
        if (command is null) return false;
        try { return command.CanExecute(null); }
        catch { return false; }
    }

    private static string Status(CommandResolution resolution, bool hasSite)
    {
        if (resolution.Command is null) return resolution.Detail;
        var prefix = resolution.Detail + " ";
        return prefix + (SafeCanExecute(resolution.Command)
            ? "Ready to run."
            : hasSite
                ? "Available but requires a compatible selection or loaded data."
                : "Available; select a website first for site-dependent actions.");
    }

    private static Button HeaderButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Margin = new Thickness(5, 0, 0, 0),
        Padding = new Thickness(10, 4, 10, 4),
        MinHeight = 26
    };

    private static Button ActionButton(string content) => new()
    {
        Content = content,
        Margin = new Thickness(7, 0, 0, 0),
        Padding = new Thickness(12, 7, 12, 7),
        MinWidth = 100
    };

    private static string Symbol(string state) => state == "Ready" ? "✓" : state == "Blocked" ? "○" : "✕";
    private static Brush StateBrush(string state) => state == "Ready" ? Brushes.SeaGreen : state == "Blocked" ? Brushes.DarkGoldenrod : Brushes.IndianRed;

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            if (panel.Children.OfType<FrameworkElement>().SelectMany(Enumerate<TextBlock>)
                .Any(x => x.Text?.Contains("Active:", StringComparison.OrdinalIgnoreCase) == true)) return panel;
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

    private sealed record ScreenDefinition(string ViewModelProperty, params ScreenAction[] Actions);
    private sealed record ScreenAction(string Label, bool IsDestructive, params string[] CommandNames);
    private sealed record CommandResolution(ICommand? Command, string? ResolvedName, string Detail);
}
