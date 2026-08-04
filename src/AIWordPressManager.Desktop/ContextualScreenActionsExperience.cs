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

    private static readonly IReadOnlyDictionary<string, ScreenActionDefinition> Registry =
        new Dictionary<string, ScreenActionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sites"] = new(nameof(MainWindowViewModel.Sites),
            [
                new("Refresh sites", "LoadCommand", false),
                new("Add website", "AddCommand", false),
                new("Test connection", "TestSelectedCommand", false),
                new("Edit website", "EditSelectedCommand", false),
                new("Remove website", "DeleteSelectedCommand", true)
            ]),
            ["WordPress Explorer"] = new(nameof(MainWindowViewModel.Explorer),
            [
                new("Load local snapshot", "LoadCommand", false),
                new("Synchronize now", "RefreshCommand", false),
                new("Clear filters", "ClearFiltersCommand", false),
                new("Open selected content", "OpenSelectedContentCommand", false),
                new("Open selected media", "OpenSelectedMediaCommand", false)
            ]),
            ["SEO Audit"] = new(nameof(MainWindowViewModel.SeoAudit),
            [
                new("Run SEO audit", "RunAuditCommand", false),
                new("Open selected issue", "OpenSelectedCommand", false),
                new("Copy selected link", "CopySelectedLinkCommand", false)
            ]),
            ["Suggested Changes"] = new(nameof(MainWindowViewModel.SuggestedChanges),
            [
                new("Refresh proposals", "RefreshCommand", false),
                new("Generate proposals", "GenerateCommand", false),
                new("Show pending", "ShowPendingCommand", false),
                new("Show approved", "ShowApprovedCommand", false),
                new("Approve selected", "BulkApproveCommand", true),
                new("Apply safe selected", "ApplySafeSelectedCommand", true)
            ]),
            ["Execution Center"] = new(nameof(MainWindowViewModel.ExecutionCenter),
            [
                new("Refresh queue", "LoadCommand", false),
                new("Prepare supported", "PrepareAllSupportedCommand", false),
                new("Execute all ready", "ExecuteAllReadyCommand", true),
                new("Retry failed", "RetryFailedCommand", true)
            ]),
            ["Backups"] = new(nameof(MainWindowViewModel.Backups),
            [
                new("Refresh backups", "LoadCommand", false),
                new("Create backup", "CreateBackupCommand", false),
                new("Import backup", "ImportBackupCommand", false),
                new("Export selected", "ExportSelectedCommand", false),
                new("Restore selected", "RestoreSelectedCommand", true),
                new("Restore from file", "RestoreFromFileCommand", true)
            ]),
            ["Health Center"] = new(nameof(MainWindowViewModel.HealthCenter),
            [
                new("Refresh health", "LoadCommand", false),
                new("Run checks", "RefreshCommand", false)
            ]),
            ["Transaction Center"] = new(nameof(MainWindowViewModel.TransactionCenter),
            [
                new("Load transactions", "LoadCommand", false),
                new("Reconcile selected", "ReconcileCommand", true),
                new("Export CSV", "ExportCsvCommand", false),
                new("Open journal folder", "OpenJournalFolderCommand", false)
            ]),
            ["Evidence Center"] = new(nameof(MainWindowViewModel.EvidenceCenter),
            [
                new("Load evidence", "LoadCommand", false),
                new("Refresh evidence", "RefreshCommand", false)
            ])
        };

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

        var actions = HeaderButton("Actions ▾", "Open the actions available for the current screen");
        actions.Click += (_, _) => OpenContextActions(actions, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), actions);

        var map = HeaderButton("Function Map", "Review every registered screen function and its current readiness");
        map.Click += (_, _) => ShowFunctionMap(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), map);

        window.PreviewKeyDown += (_, args) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (args.Key == Key.OemPeriod)
            {
                args.Handled = true;
                OpenContextActions(actions, main);
            }
        };
    }

    private static void OpenContextActions(Button owner, MainWindowViewModel main)
    {
        var menu = new ContextMenu();
        var page = main.CurrentPage;

        if (!Registry.TryGetValue(page, out var definition))
        {
            menu.Items.Add(new MenuItem
            {
                Header = $"No registered quick actions for {page}",
                IsEnabled = false
            });
        }
        else
        {
            var viewModel = ResolveViewModel(main, definition.ViewModelProperty);
            foreach (var action in definition.Actions)
            {
                var resolved = ResolveCommand(viewModel, action.CommandName);
                var item = new MenuItem
                {
                    Header = action.Label,
                    IsEnabled = resolved.Command?.CanExecute(null) == true,
                    ToolTip = BuildStatusText(resolved, main.Sites.SelectedSite is not null),
                    Tag = action.CommandName
                };

                if (action.IsDestructive)
                    item.Foreground = Brushes.IndianRed;

                item.Click += async (_, _) =>
                {
                    if (resolved.Command is null || !resolved.Command.CanExecute(null)) return;
                    try
                    {
                        var result = resolved.Command.Execute(null);
                        if (result is Task task) await task;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(owner, ex.ToString(), $"{action.Label} failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var functionMap = new MenuItem { Header = "Open Function Map" };
        functionMap.Click += (_, _) =>
        {
            var window = Window.GetWindow(owner);
            if (window is not null) ShowFunctionMap(window, main);
        };
        menu.Items.Add(functionMap);

        owner.ContextMenu = menu;
        menu.PlacementTarget = owner;
        menu.IsOpen = true;
    }

    private static void ShowFunctionMap(Window owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Screen function map",
            Width = 980,
            Height = 720,
            MinWidth = 700,
            MinHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = Brush("SurfaceBrush", Brushes.White)
        };

        var root = new DockPanel { Margin = new Thickness(18) };
        dialog.Content = root;

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var copy = ActionButton("Copy report");
        var close = ActionButton("Close");
        footer.Children.Add(copy);
        footer.Children.Add(close);

        var content = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        content.Children.Add(new TextBlock
        {
            Text = "Screen function map",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Shows whether every registered command exists and whether it is currently enabled. Blocked usually means a website or row selection is required.",
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        var report = new StringBuilder()
            .AppendLine("AI WORDPRESS MANAGER — SCREEN FUNCTION MAP")
            .AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Selected website: {main.DashboardSelectedSite}")
            .AppendLine();

        foreach (var pair in Registry)
        {
            var definition = pair.Value;
            var viewModel = ResolveViewModel(main, definition.ViewModelProperty);
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
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("TextPrimaryBrush", Brushes.Black)
            });
            report.AppendLine($"[{pair.Key}]");

            foreach (var action in definition.Actions)
            {
                var result = ResolveCommand(viewModel, action.CommandName);
                var state = result.Command is null ? "Missing" : result.Command.CanExecute(null) ? "Ready" : "Blocked";
                var detail = BuildStatusText(result, main.Sites.SelectedSite is not null);
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

    private static object? ResolveViewModel(MainWindowViewModel main, string propertyName) =>
        typeof(MainWindowViewModel).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(main);

    private static CommandResolution ResolveCommand(object? viewModel, string commandName)
    {
        if (viewModel is null) return new(null, "ViewModel is unavailable.");
        var property = viewModel.GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null) return new(null, $"Command property '{commandName}' is missing.");
        if (property.GetValue(viewModel) is not ICommand command)
            return new(null, $"Property '{commandName}' does not implement ICommand.");
        return new(command, "Command is registered.");
    }

    private static string BuildStatusText(CommandResolution resolution, bool hasSite)
    {
        if (resolution.Command is null) return resolution.Detail;
        try
        {
            return resolution.Command.CanExecute(null)
                ? "Ready to run."
                : hasSite
                    ? "Available but requires a compatible row, selection, or loaded data."
                    : "Available; select a website first for site-dependent actions.";
        }
        catch (Exception ex)
        {
            return $"CanExecute failed: {ex.Message}";
        }
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

    private static string Symbol(string state) => state switch
    {
        "Ready" => "✓",
        "Blocked" => "○",
        _ => "✕"
    };

    private static Brush StateBrush(string state) => state switch
    {
        "Ready" => Brushes.SeaGreen,
        "Blocked" => Brushes.DarkGoldenrod,
        _ => Brushes.IndianRed
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

    private sealed record ScreenActionDefinition(string ViewModelProperty, ScreenAction[] Actions);
    private sealed record ScreenAction(string Label, string CommandName, bool IsDestructive);
    private sealed record CommandResolution(ICommand? Command, string Detail);
}
