using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class QuickNavigationExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly string[] Pages =
    [
        "Sites", "Dashboard", "WordPress Explorer", "SEO Audit", "Suggested Changes",
        "Approval Queue", "Execution Center", "Backups", "Health Center",
        "Transaction Center", "Evidence Center", "Activity Timeline", "Jobs",
        "Reports", "Logs", "Notifications", "Settings", "Help"
    ];

    private static readonly IReadOnlyDictionary<string, string> ParentPages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "Sites",
            ["WordPress Explorer"] = "Sites",
            ["SEO Audit"] = "WordPress Explorer",
            ["Suggested Changes"] = "SEO Audit",
            ["Approval Queue"] = "Suggested Changes",
            ["Execution Center"] = "Approval Queue",
            ["Transaction Center"] = "Execution Center",
            ["Evidence Center"] = "Execution Center",
            ["Activity Timeline"] = "Dashboard",
            ["Backups"] = "Sites",
            ["Health Center"] = "Dashboard",
            ["Jobs"] = "Execution Center",
            ["Reports"] = "Dashboard",
            ["Logs"] = "Jobs",
            ["Notifications"] = "Dashboard",
            ["Settings"] = "Sites",
            ["Help"] = "Sites"
        };

    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var host = FindTopBar(root);
        if (host is null) return;
        Attached.Add(window, new object());

        var home = NavigationButton("⌂", "Go to Sites");
        var up = NavigationButton("↑", "Go to the logical parent page");
        var switcher = new Button
        {
            Content = "Quick switch",
            ToolTip = "Search and open any page (Ctrl+K)",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26
        };

        home.Click += async (_, _) => await NavigateAsync(main, "Sites");
        up.Click += async (_, _) =>
        {
            var target = ResolveParent(main.CurrentPage);
            await NavigateAsync(main, target);
        };
        switcher.Click += (_, _) => ShowQuickSwitcher(window, main);

        host.Children.Insert(0, switcher);
        host.Children.Insert(0, up);
        host.Children.Insert(0, home);

        void RefreshUp()
        {
            var target = ResolveParent(main.CurrentPage);
            up.IsEnabled = !string.Equals(main.CurrentPage, target, StringComparison.OrdinalIgnoreCase);
            up.ToolTip = up.IsEnabled ? $"Up to {target}" : "Already at the top level";
        }

        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage)) RefreshUp();
        };

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.K || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            args.Handled = true;
            ShowQuickSwitcher(window, main);
        };

        RefreshUp();
    }

    private static void ShowQuickSwitcher(Window owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Quick switch",
            Width = 620,
            Height = 520,
            MinWidth = 480,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = ResourceBrush("SurfaceBrush", Brushes.White)
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        dialog.Content = root;

        var search = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 14,
            ToolTip = "Type a page name"
        };
        DockPanel.SetDock(search, Dock.Top);
        root.Children.Add(search);

        var status = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        var list = new ListBox
        {
            ItemsSource = Pages,
            SelectedIndex = 0,
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray)
        };
        root.Children.Add(list);

        void Filter()
        {
            var term = (search.Text ?? string.Empty).Trim();
            var filtered = Pages
                .Where(page => string.IsNullOrWhiteSpace(term) ||
                               page.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            list.ItemsSource = filtered;
            list.SelectedIndex = filtered.Length > 0 ? 0 : -1;
            status.Text = filtered.Length == 0
                ? "No matching pages"
                : $"{filtered.Length} page(s) • Enter to open • Esc to close";
        }

        async Task OpenSelectedAsync()
        {
            if (list.SelectedItem is not string page) return;
            dialog.Close();
            await NavigateAsync(main, page);
        }

        search.TextChanged += (_, _) => Filter();
        search.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Down)
            {
                args.Handled = true;
                if (list.Items.Count > 0)
                    list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
            }
            else if (args.Key == Key.Up)
            {
                args.Handled = true;
                if (list.Items.Count > 0)
                    list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
            }
            else if (args.Key == Key.Enter)
            {
                args.Handled = true;
                await OpenSelectedAsync();
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                dialog.Close();
            }
        };
        list.MouseDoubleClick += async (_, _) => await OpenSelectedAsync();

        dialog.Loaded += (_, _) => search.Focus();
        Filter();
        dialog.ShowDialog();
    }

    private static string ResolveParent(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return "Sites";
        return ParentPages.TryGetValue(page.Trim(), out var parent) ? parent : "Sites";
    }

    private static async Task NavigateAsync(MainWindowViewModel main, string page)
    {
        if (string.IsNullOrWhiteSpace(page) || !main.NavigateCommand.CanExecute(page)) return;
        await main.NavigateCommand.ExecuteAsync(page);
    }

    private static Button NavigationButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Width = 34,
        Height = 28,
        Margin = new Thickness(0, 0, 5, 0),
        Padding = new Thickness(0),
        FontSize = 16,
        FontWeight = FontWeights.Bold
    };

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            if (panel.Children.OfType<FrameworkElement>()
                .SelectMany(Enumerate<TextBlock>)
                .Any(x => x.Text?.Contains("Active:", StringComparison.OrdinalIgnoreCase) == true))
                return panel;
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

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
