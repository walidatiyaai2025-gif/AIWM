using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Removes duplicate floating SEO helper cards that cover the main SEO workspace.
/// Their actions already exist in the page header and workflow bar, so the legacy
/// copies are hidden instead of being allowed to overlap tables and metrics.
/// </summary>
internal static class SeoWorkspaceOverlapGuard
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly string[] Markers =
    [
        "Real content analysis",
        "Journey completion",
        "Priority resolution workspace",
        "Review workbenches"
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
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main) return;

        var state = new State(window, main);
        Attached.Add(window, state);
        main.PropertyChanged += state.OnMainPropertyChanged;
        window.Closed += state.OnClosed;
        window.Dispatcher.BeginInvoke(new Action(state.Apply));
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element) return;
        var window = Window.GetWindow(element) as MainWindow;
        if (window is null || !Attached.TryGetValue(window, out var state)) return;
        if (!state.IsSeoPage) return;

        TrySuppress(element);
    }

    private static void TrySuppress(FrameworkElement element)
    {
        if (element is not Border and not ContentControl) return;
        if (element.Tag?.ToString() is "PrimaryWorkActionBar" or "ProfessionalStatusBar" or "DockedExecutionNotice") return;

        var text = ReadText(element);
        if (!Markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase))) return;

        // Only suppress compact helper cards. Never collapse the page root or a large workspace container.
        var width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        var height = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        var compact = (double.IsNaN(width) || width <= 620) && (double.IsNaN(height) || height <= 560);
        var taggedHelper = element.Tag?.ToString() is string tag &&
                           (tag.Contains("Panel", StringComparison.OrdinalIgnoreCase) ||
                            tag.Contains("Workspace", StringComparison.OrdinalIgnoreCase) ||
                            tag.Contains("Overlay", StringComparison.OrdinalIgnoreCase));
        if (!compact && !taggedHelper) return;

        element.Visibility = Visibility.Collapsed;
        element.IsHitTestVisible = false;
        element.Focusable = false;
        Panel.SetZIndex(element, -1000);
    }

    private static string ReadText(DependencyObject root)
    {
        var values = new List<string>();
        foreach (var item in Enumerate<DependencyObject>(root))
        {
            switch (item)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    values.Add(text.Text);
                    break;
                case ContentControl control when control.Content is string value && !string.IsNullOrWhiteSpace(value):
                    values.Add(value);
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

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        public bool IsSeoPage => main.CurrentPage.Equals("SEO Audit", StringComparison.OrdinalIgnoreCase);

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                window.Dispatcher.BeginInvoke(new Action(Apply));
        }

        public void Apply()
        {
            if (!IsSeoPage || !window.IsLoaded || window.Content is not DependencyObject root) return;
            foreach (var element in Enumerate<FrameworkElement>(root).ToArray())
                TrySuppress(element);
        }

        public void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Closed -= OnClosed;
        }
    }
}
