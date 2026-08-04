using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the main header compact without intercepting the Loaded event of every WPF element.
/// The previous global FrameworkElement handler could inspect and collapse a parent container
/// while the visual tree was still being created, resulting in a fully black main window.
/// </summary>
internal static class CompactHeaderAndOverlayDiagnosticsExperience
{
    private static readonly ConditionalWeakTable<MainWindow, HeaderState> Attached = new();

    private static readonly string[] SecondaryButtonLabels =
    [
        "Recommendations",
        "Recovery",
        "Pin",
        "Pinned",
        "Recent",
        "Developer Tools"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Only observe MainWindow. Never register a class handler for every FrameworkElement.
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (Attached.TryGetValue(window, out _))
            return;

        if (window.Content is not DependencyObject root)
            return;

        // Allow WPF to finish creating and measuring the complete visual tree first.
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Attached.TryGetValue(window, out _))
                return;

            var host = FindTopBar(root);
            if (host is null)
                return;

            var state = new HeaderState(window, host);
            Attached.Add(window, state);
            state.ApplyCompactMode();
            WriteStartupDiagnostic("Compact header initialized safely.");
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private sealed class HeaderState(MainWindow window, StackPanel host)
    {
        private readonly Dictionary<string, Button> _hiddenButtons = new(StringComparer.OrdinalIgnoreCase);
        private Button? _moreButton;

        public void ApplyCompactMode()
        {
            CollectSecondaryButtons();
            if (_hiddenButtons.Count == 0 || _moreButton is not null)
                return;

            _moreButton = new Button
            {
                Content = "More ▾",
                ToolTip = "Secondary navigation and diagnostic commands",
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4),
                MinHeight = 26,
                Tag = "CompactHeaderMoreButton"
            };

            _moreButton.Click += (_, _) => OpenMenu();
            host.Children.Insert(Math.Max(0, host.Children.Count - 1), _moreButton);
            window.SizeChanged += (_, _) => RefreshCompactMode();
        }

        private void RefreshCompactMode()
        {
            CollectSecondaryButtons();
            if (_moreButton is not null)
                _moreButton.Visibility = _hiddenButtons.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void CollectSecondaryButtons()
        {
            foreach (var button in host.Children.OfType<Button>().ToArray())
            {
                if (ReferenceEquals(button, _moreButton))
                    continue;

                var label = Convert.ToString(button.Content)?.Trim();
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                var key = SecondaryButtonLabels.FirstOrDefault(x =>
                    label.Contains(x, StringComparison.OrdinalIgnoreCase));

                if (key is null)
                    continue;

                _hiddenButtons[key] = button;
                button.Visibility = Visibility.Collapsed;
                button.IsTabStop = false;
            }
        }

        private void OpenMenu()
        {
            if (_moreButton is null)
                return;

            var menu = new ContextMenu { PlacementTarget = _moreButton };

            foreach (var pair in _hiddenButtons.OrderBy(x => Array.IndexOf(SecondaryButtonLabels, x.Key)))
            {
                var source = pair.Value;
                var item = new MenuItem
                {
                    Header = Convert.ToString(source.Content) ?? pair.Key,
                    IsEnabled = source.IsEnabled,
                    ToolTip = source.ToolTip
                };

                item.Click += (_, _) =>
                {
                    if (source.IsEnabled)
                        source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, source));
                };

                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var openLog = new MenuItem { Header = "Open UI startup diagnostics log" };
            openLog.Click += (_, _) => OpenDiagnosticsLog();
            menu.Items.Add(openLog);

            _moreButton.ContextMenu = menu;
            menu.IsOpen = true;
        }
    }

    private static void WriteStartupDiagnostic(string message)
    {
        try
        {
            var file = GetDiagnosticsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(
                file,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never interrupt startup.
        }
    }

    private static void OpenDiagnosticsLog()
    {
        try
        {
            var file = GetDiagnosticsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            if (!File.Exists(file))
                File.WriteAllText(file, "No UI startup diagnostics have been recorded yet." + Environment.NewLine, Encoding.UTF8);

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch
        {
            // Opening diagnostics is optional.
        }
    }

    private static string GetDiagnosticsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "Logs",
        "ui-startup.log");

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal)
                continue;

            var hasActiveLabel = panel.Children
                .OfType<FrameworkElement>()
                .SelectMany(Enumerate<TextBlock>)
                .Any(x => x.Text?.Contains("Active:", StringComparison.OrdinalIgnoreCase) == true);

            if (hasActiveLabel)
                return panel;
        }

        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed)
            yield return typed;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child))
                yield return nested;
        }
    }
}
