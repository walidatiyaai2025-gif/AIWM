using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class LiveDiagnosticsCenterExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var exception = e.Exception.Flatten();
        ProfessionalNotificationCenter.Publish(
            NotificationSeverity.Error,
            "Background task failed",
            exception.InnerExceptions.FirstOrDefault()?.Message ?? exception.Message,
            exception.ToString(),
            "TaskScheduler");
        e.SetObserved();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception) return;
        ProfessionalNotificationCenter.Publish(
            NotificationSeverity.Error,
            "Unhandled application error",
            exception.Message,
            exception.ToString(),
            "AppDomain");
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window.Content is not Grid root)
            return;

        if (FindNamedElement<Button>(root, "LiveDiagnosticsButton") is not null)
            return;

        var button = new Button
        {
            Name = "LiveDiagnosticsButton",
            Content = "◉",
            ToolTip = "Live Diagnostics Center",
            Width = 42,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 60, 0),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        button.SetResourceReference(Control.ForegroundProperty, "PrimaryBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "BorderStrongBrush");
        button.Click += (_, _) => LiveDiagnosticsCenterWindow.ShowFor(window);
        Panel.SetZIndex(button, 9500);
        root.Children.Add(button);
    }

    private static T? FindNamedElement<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var result = FindNamedElement<T>(VisualTreeHelper.GetChild(root, index), name);
            if (result is not null) return result;
        }
        return null;
    }
}

internal sealed class LiveDiagnosticsCenterWindow : Window
{
    private static LiveDiagnosticsCenterWindow? _current;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _memory = new();
    private readonly TextBlock _windows = new();
    private readonly TextBlock _threads = new();
    private readonly TextBlock _timers = new();
    private readonly TextBlock _dispatcher = new();
    private readonly TextBlock _uptime = new();
    private readonly TextBox _details = new();
    private readonly Stopwatch _uptimeWatch = Stopwatch.StartNew();

    private LiveDiagnosticsCenterWindow()
    {
        Title = "Live Diagnostics Center — مركز التشخيص المباشر";
        Width = 980;
        Height = 680;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Live Diagnostics",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(title);

        var cards = new UniformGrid
        {
            Columns = 3,
            Margin = new Thickness(0, 0, 0, 16)
        };
        cards.Children.Add(CreateCard("Memory", _memory));
        cards.Children.Add(CreateCard("Open windows", _windows));
        cards.Children.Add(CreateCard("Process threads", _threads));
        cards.Children.Add(CreateCard("Active timers", _timers));
        cards.Children.Add(CreateCard("Dispatcher queue", _dispatcher));
        cards.Children.Add(CreateCard("Session uptime", _uptime));
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.NoWrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _details.FontSize = 12;
        _details.Padding = new Thickness(14);
        Grid.SetRow(_details, 2);
        root.Children.Add(_details);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var refresh = CreateButton("Refresh now");
        refresh.Click += (_, _) => RefreshSnapshot();
        var collect = CreateButton("Collect memory");
        collect.Margin = new Thickness(8, 0, 0, 0);
        collect.Click += (_, _) =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ProfessionalNotificationCenter.Publish(
                NotificationSeverity.Success,
                "Memory collection completed",
                "A full managed garbage collection cycle completed.",
                null,
                "Live Diagnostics");
            RefreshSnapshot();
        };
        var copy = CreateButton("Copy report");
        copy.Margin = new Thickness(8, 0, 0, 0);
        copy.Click += (_, _) => Clipboard.SetText(_details.Text);
        actions.Children.Add(refresh);
        actions.Children.Add(collect);
        actions.Children.Add(copy);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        Content = root;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += (_, _) => RefreshSnapshot();
        Loaded += (_, _) =>
        {
            RefreshSnapshot();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _current = null;
        };
    }

    internal static void ShowFor(Window owner)
    {
        if (_current is { IsVisible: true })
        {
            _current.Activate();
            return;
        }

        _current = new LiveDiagnosticsCenterWindow { Owner = owner };
        _current.Show();
    }

    private void RefreshSnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var openWindows = Application.Current?.Windows.OfType<Window>().Count(window => window.IsVisible) ?? 0;
            var timers = CountDispatcherTimers();
            var pendingDispatcher = Dispatcher.CurrentDispatcher.HasShutdownStarted ? "Stopping" : "Responsive";

            _memory.Text = $"{process.WorkingSet64 / 1024d / 1024d:N1} MB";
            _windows.Text = openWindows.ToString("N0");
            _threads.Text = process.Threads.Count.ToString("N0");
            _timers.Text = timers.ToString("N0");
            _dispatcher.Text = pendingDispatcher;
            _uptime.Text = _uptimeWatch.Elapsed.ToString(@"hh\:mm\:ss");

            _details.Text = string.Join(Environment.NewLine,
                $"Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Process: {process.ProcessName} (PID {process.Id})",
                $"Working set: {process.WorkingSet64 / 1024d / 1024d:N2} MB",
                $"Private memory: {process.PrivateMemorySize64 / 1024d / 1024d:N2} MB",
                $"Managed memory: {GC.GetTotalMemory(false) / 1024d / 1024d:N2} MB",
                $"GC Gen0/1/2: {GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}",
                $"Threads: {process.Threads.Count}",
                $"Open visible windows: {openWindows}",
                $"Discovered DispatcherTimers: {timers}",
                $"Dispatcher state: {pendingDispatcher}",
                $"Session uptime: {_uptimeWatch.Elapsed:hh\:mm\:ss}",
                string.Empty,
                "Visible windows:",
                string.Join(Environment.NewLine,
                    (Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>())
                    .Where(window => window.IsVisible)
                    .Select(window => $"  - {window.GetType().Name}: {window.Title}")));
        }
        catch (Exception ex)
        {
            _details.Text = ex.ToString();
        }
    }

    private static int CountDispatcherTimers()
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var count = 0;
        foreach (Window window in Application.Current?.Windows ?? Array.Empty<Window>())
        {
            count += CountTimersInObject(window, visited, 0);
            if (window.DataContext is not null)
                count += CountTimersInObject(window.DataContext, visited, 0);
        }
        return count;
    }

    private static int CountTimersInObject(object instance, HashSet<object> visited, int depth)
    {
        if (depth > 3 || !visited.Add(instance)) return 0;
        var count = 0;
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in instance.GetType().GetFields(flags))
        {
            object? value;
            try { value = field.GetValue(instance); } catch { continue; }
            if (value is DispatcherTimer timer)
            {
                if (timer.IsEnabled) count++;
                continue;
            }
            if (value is null || field.FieldType.IsPrimitive || field.FieldType == typeof(string)) continue;
            if (field.FieldType.Namespace?.StartsWith("AIWordPressManager", StringComparison.Ordinal) == true)
                count += CountTimersInObject(value, visited, depth + 1);
        }
        return count;
    }

    private static Border CreateCard(string title, TextBlock value)
    {
        value.FontSize = 21;
        value.FontWeight = FontWeights.Bold;
        value.Margin = new Thickness(0, 6, 0, 0);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Opacity = .68,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(value);

        var border = new Border
        {
            Margin = new Thickness(4),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        return border;
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 120,
            Height = 38,
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        return button;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static ReferenceEqualityComparer Instance { get; } = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
