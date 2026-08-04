using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ProfessionalStatusBarExperience
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

        Attached.Add(window, new object());

        var bar = BuildBar(main);
        Grid.SetRow(bar, 5);
        Panel.SetZIndex(bar, 100);

        // Replace legacy footer visuals in the dedicated status row. Nothing is placed
        // over the page content, so clicks can never leak through an overlay.
        foreach (var child in root.Children.OfType<FrameworkElement>()
                     .Where(x => Grid.GetRow(x) == 5)
                     .ToArray())
        {
            child.Visibility = Visibility.Collapsed;
            child.IsHitTestVisible = false;
        }

        root.Children.Add(bar);
    }

    private static Border BuildBar(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Height = 30,
            Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 0),
            Tag = "ProfessionalStatusBar"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shell.Child = grid;

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(left);

        left.Children.Add(StatusText("●", "SuccessBrush", "DatabaseStatus", bold: true));
        left.Children.Add(StatusText(string.Empty, "TextSecondaryBrush", "DatabaseStatus"));
        left.Children.Add(Separator());

        var site = StatusButton("◉", "DashboardSelectedSite", async () =>
            await main.NavigateCommand.ExecuteAsync("Sites"));
        site.ToolTip = "Open website management";
        left.Children.Add(site);
        left.Children.Add(Separator());

        left.Children.Add(StatusText("AI", "PrimaryBrush", null, bold: true));
        left.Children.Add(StatusText(string.Empty, "TextSecondaryBrush", "DashboardSeoScoreState"));
        left.Children.Add(Separator());

        var queue = StatusButton("Queue:", "DashboardQueueTotal", async () =>
            await main.NavigateCommand.ExecuteAsync("Execution Center"));
        queue.ToolTip = "Open execution queue";
        left.Children.Add(queue);

        var running = StatusButton("Running:", "DashboardRunningJobs", async () =>
            await main.NavigateCommand.ExecuteAsync("Execution Center"));
        running.ToolTip = "Open running operations";
        left.Children.Add(running);

        var errors = StatusButton("Errors:", "DashboardFailedJobs", async () =>
            await main.NavigateCommand.ExecuteAsync("Jobs"));
        errors.ToolTip = "Open failed jobs";
        left.Children.Add(errors);

        var right = new StackPanel
        {
            Grid.IsSharedSizeScopeProperty = { },
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        right.Children.Add(StatusText("Last sync:", "TextSecondaryBrush", null));
        right.Children.Add(StatusText(string.Empty, "TextPrimaryBrush", "DashboardLastSiteSync", bold: true));
        right.Children.Add(Separator());
        right.Children.Add(StatusText("v", "TextSecondaryBrush", null));
        right.Children.Add(VersionText());

        return shell;
    }

    private static TextBlock StatusText(string prefix, string brushKey, string? bindingPath, bool bold = false)
    {
        var text = new TextBlock
        {
            Text = prefix,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(brushKey, Brushes.DimGray)
        };
        if (!string.IsNullOrWhiteSpace(bindingPath))
            text.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
        return text;
    }

    private static Button StatusButton(string prefix, string bindingPath, Func<Task> action)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = prefix,
            Margin = new Thickness(0, 0, 3, 0),
            FontSize = 11,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        var value = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        };
        value.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
        panel.Children.Add(value);

        var button = new Button
        {
            Content = panel,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(4, 2, 4, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 14,
        Margin = new Thickness(7, 0, 9, 0),
        Background = Brush("BorderBrush", Brushes.LightGray),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock VersionText()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        return new TextBlock
        {
            Text = version is null ? "1.9.0" : $"{version.Major}.{version.Minor}.{version.Build}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
