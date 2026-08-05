using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Enforces the work-page layout contract without polling the visual tree.
/// Page changes trigger one deferred surface cleanup, while execution counters
/// update only when their existing ViewModel properties change.
/// </summary>
internal static class WorkPageOverlayGuard
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly string[] BlockedTextMarkers =
    [
        "approved change(s) ready for execution",
        "approved changes ready for execution",
        "Guided workspace",
        "Journey completion",
        "Quick Fix Queue",
        "Priority resolution workspace",
        "Review workbenches",
        "AI Copilot Inbox",
        "Live operations"
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
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var state = new State(window, root, main);
        Attached.Add(window, state);

        main.PropertyChanged += state.OnMainPropertyChanged;
        main.ExecutionCenter.PropertyChanged += state.OnExecutionCenterPropertyChanged;
        window.ContentRendered += state.OnContentRendered;
        window.Closed += state.OnClosed;

        ScheduleApply(state, includeSurfaceScan: true);
    }

    private static void ScheduleApply(State state, bool includeSurfaceScan)
    {
        if (state.IsDisposed) return;

        if (includeSurfaceScan)
            state.SurfaceScanPending = true;

        if (state.ApplyPending) return;
        state.ApplyPending = true;

        _ = state.Window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            state.ApplyPending = false;
            if (state.IsDisposed || !state.Window.IsLoaded) return;

            if (state.SurfaceScanPending)
            {
                state.SurfaceScanPending = false;
                RetireBlockedSurfaces(state.Root);
            }

            EnsureCompactExecutionStatus(state);
        }));
    }

    private static void RetireBlockedSurfaces(DependencyObject root)
    {
        foreach (var element in Enumerate<FrameworkElement>(root).ToArray())
        {
            if (element.Tag?.ToString() is string tag &&
                (tag.Contains("Panel", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("Workspace", StringComparison.OrdinalIgnoreCase)
                 || tag.Contains("Overlay", StringComparison.OrdinalIgnoreCase)))
            {
                if (tag is "PrimaryWorkActionBar" or "ProfessionalStatusBar")
                    continue;

                if (tag.Contains("PriorityResolution", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("ReviewWorkbenches", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("QuickFixJourney", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("ContentQualityBatch", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("MediaAnalysis", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("AiCopilotInbox", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("FloatingWorkspace", StringComparison.OrdinalIgnoreCase))
                {
                    FloatingWorkspaceManager.Retire(element);
                    continue;
                }
            }

            if (element is Border or ContentControl)
            {
                var text = ReadDescendantText(element);
                if (BlockedTextMarkers.Any(marker =>
                        text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    FloatingWorkspaceManager.Retire(element);
                }
            }
        }
    }

    private static void EnsureCompactExecutionStatus(State state)
    {
        var actionBar = state.ActionBar;
        if (actionBar is null || !actionBar.IsLoaded)
        {
            actionBar = FindByTag<Border>(state.Root, "PrimaryWorkActionBar");
            state.ActionBar = actionBar;
        }

        if (actionBar?.Child is not Grid grid) return;

        var status = state.ExecutionStatusButton;
        if (status is null || !status.IsLoaded)
        {
            status = FindByTag<Button>(actionBar, "CompactExecutionStatus");
            if (status is null)
            {
                status = new Button
                {
                    Tag = "CompactExecutionStatus",
                    Height = 24,
                    MinWidth = 126,
                    Margin = new Thickness(8, 0, 8, 0),
                    Padding = new Thickness(9, 2, 9, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Open Execution Center",
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                status.Click += async (_, _) =>
                    await state.Main.NavigateCommand.ExecuteAsync("Execution Center");

                Grid.SetColumn(status, 1);
                grid.Children.Add(status);
            }

            state.ExecutionStatusButton = status;
        }

        var ready = state.Main.ExecutionCenter.ReadyCount;
        var failed = state.Main.ExecutionCenter.FailedCount;
        status.Content = failed > 0
            ? $"Execution: {ready} ready • {failed} failed"
            : $"Execution: {ready} ready";
        status.Visibility = ready > 0 || failed > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        status.IsHitTestVisible = status.Visibility == Visibility.Visible;
    }

    private static string ReadDescendantText(DependencyObject root)
    {
        var values = new List<string>();
        foreach (var element in Enumerate<DependencyObject>(root))
        {
            switch (element)
            {
                case TextBlock text when !string.IsNullOrWhiteSpace(text.Text):
                    values.Add(text.Text);
                    break;
                case ContentControl control when control.Content is string value && !string.IsNullOrWhiteSpace(value):
                    values.Add(value);
                    break;
            }
        }
        return string.Join(" ", values);
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindByTag<T>(child, tag);
            if (result is not null) return result;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private sealed class State
    {
        public State(MainWindow window, Grid root, MainWindowViewModel main)
        {
            Window = window;
            Root = root;
            Main = main;
        }

        public MainWindow Window { get; }
        public Grid Root { get; }
        public MainWindowViewModel Main { get; }
        public Border? ActionBar { get; set; }
        public Button? ExecutionStatusButton { get; set; }
        public bool ApplyPending { get; set; }
        public bool SurfaceScanPending { get; set; }
        public bool IsDisposed { get; private set; }

        public void OnContentRendered(object? sender, EventArgs e) =>
            ScheduleApply(this, includeSurfaceScan: true);

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage))
            {
                ActionBar = null;
                ExecutionStatusButton = null;
                ScheduleApply(this, includeSurfaceScan: true);
            }
        }

        public void OnExecutionCenterPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "ReadyCount" or "FailedCount")
                ScheduleApply(this, includeSurfaceScan: false);
        }

        public void OnClosed(object? sender, EventArgs e)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Main.PropertyChanged -= OnMainPropertyChanged;
            Main.ExecutionCenter.PropertyChanged -= OnExecutionCenterPropertyChanged;
            Window.ContentRendered -= OnContentRendered;
            Window.Closed -= OnClosed;
        }
    }
}
