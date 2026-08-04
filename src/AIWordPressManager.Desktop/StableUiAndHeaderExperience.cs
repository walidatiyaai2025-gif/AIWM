using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Prevents legacy auto-open surfaces from flashing during background refreshes and
/// consolidates diagnostic commands into one Developer Tools menu.
/// </summary>
internal static class StableUiAndHeaderExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly string[] SuppressedSurfaceMarkers =
    [
        "guided workspace",
        "journey completion",
        "quick fix queue",
        "approved change(s) ready for execution",
        "approved changes ready for execution",
        "live operations",
        "ai copilot inbox",
        "memory cooling mode",
        "cooling memory"
    ];

    private static readonly string[] DeveloperButtonMarkers =
    [
        "function map",
        "screen check",
        "refresh screens"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        // Loaded is early enough to collapse legacy surfaces before the first useful render.
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyElementLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnAnyElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element is MainWindow) return;

        var text = ReadSurfaceText(element);
        if (string.IsNullOrWhiteSpace(text) || !ContainsAny(text, SuppressedSurfaceMarkers)) return;

        var surface = FindCollapsibleSurface(element);
        if (surface is null) return;

        surface.Visibility = Visibility.Collapsed;
        surface.IsHitTestVisible = false;
        surface.Focusable = false;
        Panel.SetZIndex(surface, -10000);

        // Popup children live in a separate presentation source. Closing the owning popup
        // prevents the one-frame flash that occurred when a timer reopened it.
        if (FindPopupAncestor(element) is Popup popup)
        {
            popup.StaysOpen = false;
            popup.IsOpen = false;
            popup.IsHitTestVisible = false;
        }
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.Content is not DependencyObject root) return;

        var topBar = FindTopBar(root);
        if (topBar is null) return;

        Attached.Add(window, new object());

        var developerButton = new Button
        {
            Content = "Developer Tools ▾",
            ToolTip = "Diagnostics and internal maintenance tools",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26,
            Tag = "DeveloperToolsMenu"
        };

        developerButton.Click += (_, _) => OpenDeveloperMenu(developerButton, topBar);
        topBar.Children.Insert(Math.Max(0, topBar.Children.Count - 1), developerButton);

        ConsolidateDeveloperButtons(topBar, developerButton);
    }

    private static void OpenDeveloperMenu(Button owner, Panel topBar)
    {
        ConsolidateDeveloperButtons(topBar, owner);

        var menu = new ContextMenu { PlacementTarget = owner };
        var tools = topBar.Children.OfType<Button>()
            .Where(x => !ReferenceEquals(x, owner) && x.Tag as string == "ConsolidatedDeveloperTool")
            .ToList();

        if (tools.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No developer tools available", IsEnabled = false });
        }
        else
        {
            foreach (var original in tools)
            {
                var label = Convert.ToString(original.Content)?.Trim();
                if (string.IsNullOrWhiteSpace(label)) continue;

                var item = new MenuItem
                {
                    Header = label,
                    ToolTip = original.ToolTip,
                    IsEnabled = original.IsEnabled
                };
                item.Click += (_, _) => original.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, original));
                menu.Items.Add(item);
            }
        }

        menu.IsOpen = true;
    }

    private static void ConsolidateDeveloperButtons(Panel topBar, Button developerButton)
    {
        foreach (var button in topBar.Children.OfType<Button>().ToList())
        {
            if (ReferenceEquals(button, developerButton)) continue;
            var label = Convert.ToString(button.Content)?.Trim() ?? string.Empty;
            if (!ContainsAny(label, DeveloperButtonMarkers)) continue;

            button.Tag = "ConsolidatedDeveloperTool";
            button.Visibility = Visibility.Collapsed;
            button.IsTabStop = false;
        }
    }

    private static FrameworkElement? FindCollapsibleSurface(FrameworkElement start)
    {
        FrameworkElement? candidate = start;
        var current = start;

        for (var depth = 0; depth < 8; depth++)
        {
            var parent = VisualTreeHelper.GetParent(current) as FrameworkElement
                         ?? LogicalTreeHelper.GetParent(current) as FrameworkElement;
            if (parent is null || parent is MainWindow || parent is Window) break;

            candidate = parent;
            if (parent is Border or UserControl or ContentControl or Popup) break;
            current = parent;
        }

        return candidate;
    }

    private static Popup? FindPopupAncestor(DependencyObject start)
    {
        DependencyObject? current = start;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            if (current is Popup popup) return popup;
            current = LogicalTreeHelper.GetParent(current) ??
                      (current is Visual || current is System.Windows.Media.Media3D.Visual3D
                          ? VisualTreeHelper.GetParent(current)
                          : null);
        }
        return null;
    }

    private static string ReadSurfaceText(DependencyObject root)
    {
        var values = new List<string>();
        foreach (var text in Enumerate<TextBlock>(root))
            if (!string.IsNullOrWhiteSpace(text.Text)) values.Add(text.Text);
        foreach (var control in Enumerate<ContentControl>(root))
            if (control.Content is string value && !string.IsNullOrWhiteSpace(value)) values.Add(value);
        return string.Join(" ", values);
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static Panel? FindTopBar(DependencyObject root)
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
        var count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch { }
        for (var i = 0; i < count; i++)
        {
            DependencyObject child;
            try { child = VisualTreeHelper.GetChild(root, i); } catch { continue; }
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }
}
