using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Gives every dynamic popup/overlay a consistent close action without polling.
/// Managed surfaces receive a top-right close button and can also be dismissed with Escape.
/// </summary>
internal static class GlobalPopupCloseExperience
{
    private static readonly ConditionalWeakTable<MainWindow, WindowState> Windows = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> Decorated = new();

    private static readonly string[] ManagedTextMarkers =
    [
        "approved change(s) ready for execution",
        "approved changes ready for execution",
        "priority resolution workspace",
        "review workbenches",
        "quick fix queue",
        "ai copilot inbox",
        "live operations",
        "notification center",
        "journey completion",
        "guided workspace"
    ];

    private static readonly string[] ExcludedTags =
    [
        "ProfessionalStatusBar",
        "PrimaryWorkActionBar",
        "CompleteJourneyCenter",
        "SiteWorkspaceSwitcher"
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
            new RoutedEventHandler(OnElementLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Windows.TryGetValue(window, out _)) return;

        var state = new WindowState(window);
        Windows.Add(window, state);
        window.PreviewKeyDown += state.OnPreviewKeyDown;
        window.Closed += state.OnClosed;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element || Decorated.TryGetValue(element, out _)) return;
        if (!IsManagedSurface(element)) return;

        var owner = Window.GetWindow(element) as MainWindow;
        if (owner is null || !Windows.TryGetValue(owner, out var state)) return;

        Decorated.Add(element, new object());
        AddCloseButton(element, state);
        state.Track(element);
    }

    private static bool IsManagedSurface(FrameworkElement element)
    {
        if (element is not Border and not ContentControl and not Popup) return false;

        var tag = element.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (ExcludedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;

            if (tag.Contains("Popup", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("Overlay", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("Dialog", StringComparison.OrdinalIgnoreCase) ||
                tag.Contains("Floating", StringComparison.OrdinalIgnoreCase))
                return true;

            if (tag.Contains("Panel", StringComparison.OrdinalIgnoreCase) &&
                !tag.Contains("Status", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var text = ReadText(element);
        return ManagedTextMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddCloseButton(FrameworkElement surface, WindowState state)
    {
        if (surface is Popup popup)
        {
            popup.StaysOpen = false;
            return;
        }

        if (HasCloseControl(surface)) return;

        var close = new Button
        {
            Content = "✕",
            ToolTip = "Close",
            Tag = "GlobalPopupCloseButton",
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Panel.ZIndex = 10000,
            Focusable = false
        };
        close.Click += (_, _) => CloseSurface(surface, state);

        switch (surface)
        {
            case Border border:
            {
                var existing = border.Child;
                var host = new Grid();
                if (existing is not null) host.Children.Add(existing);
                host.Children.Add(close);
                border.Child = host;
                break;
            }
            case ContentControl contentControl:
            {
                var existing = contentControl.Content;
                var host = new Grid();
                if (existing is UIElement uiElement) host.Children.Add(uiElement);
                else if (existing is not null) host.Children.Add(new ContentPresenter { Content = existing });
                host.Children.Add(close);
                contentControl.Content = host;
                break;
            }
        }
    }

    private static bool HasCloseControl(DependencyObject root)
    {
        foreach (var button in Enumerate<Button>(root))
        {
            if (Equals(button.Tag, "GlobalPopupCloseButton")) return true;
            var text = button.Content?.ToString()?.Trim();
            if (text is "✕" or "×" || string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CloseSurface(FrameworkElement surface, WindowState state)
    {
        switch (surface)
        {
            case Popup popup:
                popup.IsOpen = false;
                break;
            default:
                surface.Visibility = Visibility.Collapsed;
                surface.IsHitTestVisible = false;
                break;
        }
        state.Untrack(surface);
    }

    private static string ReadText(DependencyObject root)
    {
        var values = new List<string>();
        foreach (var element in Enumerate<DependencyObject>(root))
        {
            switch (element)
            {
                case TextBlock textBlock when !string.IsNullOrWhiteSpace(textBlock.Text):
                    values.Add(textBlock.Text);
                    break;
                case ContentControl control when control.Content is string text && !string.IsNullOrWhiteSpace(text):
                    values.Add(text);
                    break;
            }
        }
        return string.Join(' ', values);
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T current) yield return current;

        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private sealed class WindowState(MainWindow window)
    {
        private readonly List<WeakReference<FrameworkElement>> _surfaces = [];

        public void Track(FrameworkElement surface)
        {
            Cleanup();
            _surfaces.Add(new WeakReference<FrameworkElement>(surface));
        }

        public void Untrack(FrameworkElement surface)
        {
            _surfaces.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, surface));
        }

        public void OnPreviewKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape) return;

            Cleanup();
            for (var index = _surfaces.Count - 1; index >= 0; index--)
            {
                if (!_surfaces[index].TryGetTarget(out var surface)) continue;

                var isVisible = surface switch
                {
                    Popup popup => popup.IsOpen,
                    _ => surface.Visibility == Visibility.Visible && surface.IsVisible
                };
                if (!isVisible) continue;

                CloseSurface(surface, this);
                args.Handled = true;
                return;
            }

            var owned = window.OwnedWindows.Cast<Window>().LastOrDefault(child => child.IsVisible);
            if (owned is not null)
            {
                owned.Close();
                args.Handled = true;
            }
        }

        public void OnClosed(object? sender, EventArgs e)
        {
            window.PreviewKeyDown -= OnPreviewKeyDown;
            window.Closed -= OnClosed;
            _surfaces.Clear();
        }

        private void Cleanup() =>
            _surfaces.RemoveAll(reference => !reference.TryGetTarget(out _));
    }
}
