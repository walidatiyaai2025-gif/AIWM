using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Permanently retires legacy floating workspaces after their useful data and actions
/// were moved into the docked sidebar or dedicated pages. A retired element is first
/// hidden immediately, then detached from its parent so bindings cannot reopen it.
/// </summary>
internal static class FloatingWorkspaceManager
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> RetirementQueued = new();

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
        "PrimaryWorkActionBar",
        "ProfessionalStatusBar",
        "SiteWorkspaceSwitcher"
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

        Retire(element);
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

    internal static void Retire(FrameworkElement element)
    {
        var tag = element.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(tag) && AllowedDockedTags.Contains(tag)) return;

        HideImmediately(element);

        if (RetirementQueued.TryGetValue(element, out _)) return;
        RetirementQueued.Add(element, new object());

        _ = element.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => DetachFromParent(element)));
    }

    private static void HideImmediately(FrameworkElement element)
    {
        if (element is Popup popup)
        {
            popup.StaysOpen = false;
            popup.IsOpen = false;
        }

        element.Visibility = Visibility.Collapsed;
        element.IsHitTestVisible = false;
        element.Focusable = false;
        Panel.SetZIndex(element, -1000);
    }

    private static void DetachFromParent(FrameworkElement element)
    {
        HideImmediately(element);
        if (element is Popup) return;

        DependencyObject? parent = null;
        try { parent = VisualTreeHelper.GetParent(element); }
        catch { }
        parent ??= element.Parent;
        parent ??= LogicalTreeHelper.GetParent(element);

        switch (parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                return;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                return;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                return;
            case ContentPresenter presenter when ReferenceEquals(presenter.Content, element):
                presenter.Content = null;
                return;
        }

        // Some generated surfaces are re-parented by a template. Keeping them hidden
        // remains safe; the Loaded handler will retire any newly generated replacement.
    }
}
