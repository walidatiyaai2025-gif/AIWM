using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the main header compact and records every legacy surface that attempts to appear automatically.
/// Secondary commands remain accessible through a single More menu.
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

    private static readonly string[] ForbiddenSurfaceTokens =
    [
        "Guided workspace",
        "Journey completion",
        "Quick Fix Queue",
        "approved change",
        "Live operations",
        "AI Copilot Inbox",
        "MEMORY COOLING MODE",
        "Cooling memory"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyElementLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.Content is not Grid root) return;

        var host = FindTopBar(root);
        if (host is null) return;

        var state = new HeaderState(window, host);
        Attached.Add(window, state);
        state.ApplyCompactMode();
    }

    private static void OnAnyElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element is MainWindow) return;

        var text = ReadVisibleText(element);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!ForbiddenSurfaceTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase))) return;

        Suppress(element);
        WriteDiagnostic(element, text);
    }

    private static void Suppress(FrameworkElement element)
    {
        element.IsHitTestVisible = false;
        element.Focusable = false;
        element.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(element, -10000);

        if (element.Parent is Popup popup)
        {
            popup.IsOpen = false;
            popup.StaysOpen = false;
        }
    }

    private static void WriteDiagnostic(FrameworkElement element, string text)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager",
                "Logs");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "auto-overlay-attempts.log");
            var page = (Application.Current?.MainWindow?.DataContext as MainWindowViewModel)?.CurrentPage ?? "Unknown";
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | Page={page} | Type={element.GetType().FullName} | Name={element.Name} | Text={Normalize(text)}{Environment.NewLine}";
            File.AppendAllText(file, line, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never interrupt the user interface.
        }
    }

    private sealed class HeaderState(MainWindow window, StackPanel host)
    {
        private readonly Dictionary<string, Button> _hiddenButtons = new(StringComparer.OrdinalIgnoreCase);
        private Button? _moreButton;

        public void ApplyCompactMode()
        {
            CollectSecondaryButtons();
            if (_hiddenButtons.Count == 0 || _moreButton is not null) return;

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
                _moreButton.Visibility = _hiddenButtons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CollectSecondaryButtons()
        {
            foreach (var button in host.Children.OfType<Button>().ToArray())
            {
                if (ReferenceEquals(button, _moreButton)) continue;
                var label = Convert.ToString(button.Content)?.Trim();
                if (string.IsNullOrWhiteSpace(label)) continue;

                var key = SecondaryButtonLabels.FirstOrDefault(x =>
                    label.Contains(x, StringComparison.OrdinalIgnoreCase));
                if (key is null) continue;

                _hiddenButtons[key] = button;
                button.Visibility = Visibility.Collapsed;
                button.IsTabStop = false;
            }
        }

        private void OpenMenu()
        {
            if (_moreButton is null) return;
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
            var openLog = new MenuItem { Header = "Open auto-popup diagnostics log" };
            openLog.Click += (_, _) => OpenDiagnosticsLog();
            menu.Items.Add(openLog);

            _moreButton.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private static void OpenDiagnosticsLog()
        {
            try
            {
                var file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIWordPressManager",
                    "Logs",
                    "auto-overlay-attempts.log");
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (!File.Exists(file))
                    File.WriteAllText(file, "No blocked auto-overlay attempts have been recorded yet." + Environment.NewLine, Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
            }
            catch
            {
                // Opening diagnostics is optional.
            }
        }
    }

    private static string ReadVisibleText(FrameworkElement element)
    {
        if (element is TextBlock textBlock) return textBlock.Text ?? string.Empty;
        if (element is ContentControl contentControl && contentControl.Content is string text) return text;

        var builder = new StringBuilder();
        foreach (var textElement in Enumerate<TextBlock>(element).Take(25))
        {
            if (!string.IsNullOrWhiteSpace(textElement.Text))
                builder.Append(textElement.Text).Append(' ');
        }
        return builder.ToString();
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

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
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }
}
