using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Retires legacy floating workspaces after their useful data and actions were moved
/// into the docked right sidebar or dedicated pages. Elements are suppressed as they
/// load, so no periodic timer or repeated full visual-tree scan is required.
/// </summary>
internal static class FloatingWorkspaceManager
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    private static readonly HashSet<string> ManagedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "PriorityResolutionPanel",
        "ReviewWorkbenchesPanel",
        "ContentQualityBatchPanel",
        "QuickFixJourneyPanel",
        "MediaAnalysisPanel",
        "AiCopilotInboxPanel",
        "NotificationPanel",
        "NotificationToggle",
        "LiveOperationsPanel",
        "JourneyCompletionPanel",
        "RealContentAnalysisPanel",
        "ApprovedChangesPanel",
        "FloatingWorkspaceScrim",
        "FloatingWorkspaceLauncher"
    };

    private static readonly string[] ManagedTagFragments =
    [
        "AiCopilotInbox",
        "QuickFix",
        "JourneyCompletion",
        "PriorityResolution",
        "ReviewWorkbench",
        "FloatingWorkspace",
        "LiveOperations",
        "ApprovedChanges"
    ];

    private static readonly HashSet<string> AllowedDockedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "DockedRightSidebar",
        "DockedWorkspaceHost",
        "DockedWorkspaceContent",
        "DockedExecutionNotice",
        "CompleteJourneyCenter",
        "PrimaryWorkActionBar"
    };

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

        Attached.Add(window, new object());
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element) return;
        if (element is MainWindow) return;
        if (Window.GetWindow(element) is not MainWindow window || !Attached.TryGetValue(window, out _)) return;
        if (!ShouldSuppress(element)) return;

        Suppress(element);
    }

    private static bool ShouldSuppress(FrameworkElement element)
    {
        var tag = element.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tag)) return false;
        if (AllowedDockedTags.Contains(tag)) return false;
        if (ManagedTags.Contains(tag)) return true;

        return ManagedTagFragments.Any(fragment =>
            tag.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static void Suppress(FrameworkElement element)
    {
        element.Visibility = Visibility.Collapsed;
        element.IsHitTestVisible = false;
        element.Focusable = false;
        Panel.SetZIndex(element, -1000);
    }
}
