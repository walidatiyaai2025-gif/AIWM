using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ScreenRepairRefreshExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly RefreshTarget[] Targets =
    [
        new("Sites", nameof(MainWindowViewModel.Sites), false),
        new("WordPress Explorer", nameof(MainWindowViewModel.Explorer), true),
        new("SEO Audit", nameof(MainWindowViewModel.SeoAudit), true),
        new("Suggested Changes", nameof(MainWindowViewModel.SuggestedChanges), true),
        new("Execution Center", nameof(MainWindowViewModel.ExecutionCenter), true),
        new("Backups", nameof(MainWindowViewModel.Backups), true),
        new("Health Center", nameof(MainWindowViewModel.HealthCenter), true),
        new("Transaction Center", nameof(MainWindowViewModel.TransactionCenter), false),
        new("Evidence Center", nameof(MainWindowViewModel.EvidenceCenter), true)
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
            Content = "↻ Refresh screens",
            ToolTip = "Safely load local data for core screens and repair blocked screen prerequisites",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26
        };
        button.Click += async (_, _) => await ShowAsync(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), button);

        window.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key != Key.F10 ||
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;
            args.Handled = true;
            await ShowAsync(window, main);
        };
    }

    private static async Task ShowAsync(Window owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Screen repair and safe refresh",
            Width = 900,
            Height = 650,
            MinWidth = 680,
            MinHeight = 460,
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

        var content = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        content.Children.Add(new TextBlock
        {
            Text = "Screen repair and safe refresh",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Loads local screen data only. It never executes, restores, deletes, publishes, approves, or writes to WordPress.",
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        var status = new TextBlock
        {
            Text = main.Sites.SelectedSite is null
                ? "No website is selected. Repair will open Sites first."
                : $"Active website: {main.DashboardSelectedSite}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        };
        content.Children.Add(status);

        var resultsHost = new StackPanel();
        content.Children.Add(resultsHost);
        RenderInitial(resultsHost);

        var repair = ActionButton("Repair blocked screens");
        var refresh = ActionButton("Safe refresh all");
        var copy = ActionButton("Copy report");
        var close = ActionButton("Close");
        footer.Children.Add(repair);
        footer.Children.Add(refresh);
        footer.Children.Add(copy);
        footer.Children.Add(close);

        IReadOnlyList<RefreshResult> results = [];

        repair.Click += async (_, _) =>
        {
            repair.IsEnabled = refresh.IsEnabled = false;
            try
            {
                if (main.Sites.SelectedSite is null)
                {
                    dialog.Close();
                    await main.NavigateCommand.ExecuteAsync("Sites");
                    return;
                }

                results = await RefreshAllAsync(main);
                RenderResults(resultsHost, results, "Repair completed");
                status.Text = BuildSummary(results);
            }
            finally
            {
                repair.IsEnabled = refresh.IsEnabled = true;
            }
        };

        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = repair.IsEnabled = false;
            try
            {
                results = await RefreshAllAsync(main);
                RenderResults(resultsHost, results, "Safe refresh completed");
                status.Text = BuildSummary(results);
            }
            finally
            {
                refresh.IsEnabled = repair.IsEnabled = true;
            }
        };

        copy.Click += (_, _) => Clipboard.SetText(BuildReport(results, main));
        close.Click += (_, _) => dialog.Close();

        dialog.ShowDialog();
        await Task.CompletedTask;
    }

    private static async Task<IReadOnlyList<RefreshResult>> RefreshAllAsync(MainWindowViewModel main)
    {
        var originalPage = main.CurrentPage;
        var hasSite = main.Sites.SelectedSite is not null;
        var results = new List<RefreshResult>();

        foreach (var target in Targets)
        {
            if (target.RequiresSite && !hasSite)
            {
                results.Add(new RefreshResult(target.Page, RefreshState.Blocked,
                    "Select a website before loading this screen.", TimeSpan.Zero));
                continue;
            }

            var watch = Stopwatch.StartNew();
            try
            {
                var property = typeof(MainWindowViewModel).GetProperty(target.ViewModelProperty,
                    BindingFlags.Instance | BindingFlags.Public);
                var viewModel = property?.GetValue(main);
                if (viewModel is null)
                {
                    results.Add(new RefreshResult(target.Page, RefreshState.Missing,
                        $"ViewModel property '{target.ViewModelProperty}' is missing or null.", watch.Elapsed));
                    continue;
                }

                var outcome = await InvokeSafeLoadAsync(viewModel);
                results.Add(new RefreshResult(target.Page, outcome.State, outcome.Detail, watch.Elapsed));
            }
            catch (Exception ex)
            {
                results.Add(new RefreshResult(target.Page, RefreshState.Failed,
                    $"{ex.GetType().Name}: {ex.Message}", watch.Elapsed));
            }
        }

        if (!string.IsNullOrWhiteSpace(originalPage) && main.NavigateCommand.CanExecute(originalPage))
            await main.NavigateCommand.ExecuteAsync(originalPage);

        return results;
    }

    private static async Task<(RefreshState State, string Detail)> InvokeSafeLoadAsync(object viewModel)
    {
        var type = viewModel.GetType();
        var load = type.GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: Type.EmptyTypes, modifiers: null);
        if (load is not null)
        {
            var value = load.Invoke(viewModel, null);
            if (value is Task task) await task;
            return (RefreshState.Ready, "LoadAsync completed successfully.");
        }

        foreach (var name in new[] { "LoadCommand", "RefreshCommand" })
        {
            var commandProperty = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (commandProperty?.GetValue(viewModel) is not ICommand command) continue;
            if (!command.CanExecute(null))
                return (RefreshState.Blocked, $"{name} exists but its current prerequisites are not satisfied.");

            command.Execute(null);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            return (RefreshState.Ready, $"{name} was invoked safely.");
        }

        return (RefreshState.Missing, "No public LoadAsync, LoadCommand, or RefreshCommand was found.");
    }

    private static void RenderInitial(Panel host)
    {
        host.Children.Clear();
        foreach (var target in Targets)
            host.Children.Add(ResultRow(target.Page, "Waiting", Brushes.DimGray,
                target.RequiresSite ? "Requires an active website." : "Can load without an active website."));
    }

    private static void RenderResults(Panel host, IEnumerable<RefreshResult> results, string title)
    {
        host.Children.Clear();
        host.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        foreach (var result in results)
        {
            var brush = result.State switch
            {
                RefreshState.Ready => Brushes.SeaGreen,
                RefreshState.Blocked => Brushes.DarkGoldenrod,
                RefreshState.Missing or RefreshState.Failed => Brushes.IndianRed,
                _ => Brushes.DimGray
            };
            host.Children.Add(ResultRow(result.Page, result.State.ToString(), brush,
                $"{result.Detail} ({result.Elapsed.TotalMilliseconds:N0} ms)"));
        }
    }

    private static Border ResultRow(string page, string state, Brush brush, string detail)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(11),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            CornerRadius = new CornerRadius(6)
        };
        var stack = new StackPanel();
        border.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = $"{page} — {state}",
            FontWeight = FontWeights.Bold,
            Foreground = brush
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        return border;
    }

    private static string BuildSummary(IEnumerable<RefreshResult> results)
    {
        var items = results.ToArray();
        return $"Ready {items.Count(x => x.State == RefreshState.Ready)} • " +
               $"Blocked {items.Count(x => x.State == RefreshState.Blocked)} • " +
               $"Failed/Missing {items.Count(x => x.State is RefreshState.Failed or RefreshState.Missing)}";
    }

    private static string BuildReport(IEnumerable<RefreshResult> results, MainWindowViewModel main)
    {
        var builder = new StringBuilder()
            .AppendLine("AI WORDPRESS MANAGER — SAFE SCREEN REFRESH")
            .AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"Site: {main.DashboardSelectedSite}")
            .AppendLine();
        var items = results.ToArray();
        if (items.Length == 0) builder.AppendLine("No refresh has been run yet.");
        foreach (var item in items)
            builder.AppendLine($"[{item.State}] {item.Page} — {item.Detail} — {item.Elapsed.TotalMilliseconds:N0} ms");
        return builder.ToString();
    }

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

    private sealed record RefreshTarget(string Page, string ViewModelProperty, bool RequiresSite);
    private sealed record RefreshResult(string Page, RefreshState State, string Detail, TimeSpan Elapsed);
    private enum RefreshState { Ready, Blocked, Missing, Failed }
}
