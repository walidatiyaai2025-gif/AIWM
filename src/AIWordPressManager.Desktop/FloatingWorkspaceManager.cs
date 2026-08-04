using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Legacy floating workspaces are intentionally disabled. Analysis tools remain
/// available through their dedicated pages, contextual actions and AI Command Center.
/// This guard prevents old feature timers from reintroducing overlapping panels.
/// </summary>
internal static class FloatingWorkspaceManager
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly string[] ManagedTags =
    [
        "PriorityResolutionPanel",
        "ReviewWorkbenchesPanel",
        "ContentQualityBatchPanel",
        "QuickFixJourneyPanel",
        "MediaAnalysisPanel",
        "AiCopilotInboxPanel",
        "FloatingWorkspaceScrim",
        "FloatingWorkspaceLauncher"
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
        if (window.Content is not Grid root) return;

        Attached.Add(window, new object());

        var timer = new DispatcherTimer(DispatcherPriority.Send, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        timer.Tick += (_, _) => SuppressLegacyPanels(root);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        SuppressLegacyPanels(root);
    }

    private static void SuppressLegacyPanels(DependencyObject root)
    {
        foreach (var tag in ManagedTags)
        {
            foreach (var element in FindAllByTag(root, tag))
            {
                element.Visibility = Visibility.Collapsed;
                element.IsHitTestVisible = false;
                element.Focusable = false;
                Panel.SetZIndex(element, -1000);
            }
        }
    }

    private static IEnumerable<FrameworkElement> FindAllByTag(DependencyObject root, string tag)
    {
        if (root is FrameworkElement element && Equals(element.Tag, tag))
            yield return element;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var nested in FindAllByTag(child, tag))
                yield return nested;
        }
    }
}
