using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Replaces the legacy floating approved-changes card with a compact notice inside
/// the existing primary work action bar. The notice never overlays page content.
/// </summary>
internal static class DockedExecutionNoticeExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    private static readonly string[] ApprovedMarkers =
    [
        "approved change(s) ready for execution",
        "approved changes ready for execution"
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
        main.PropertyChanged += state.OnPropertyChanged;
        main.SuggestedChanges.PropertyChanged += state.OnPropertyChanged;
        main.ExecutionCenter.PropertyChanged += state.OnPropertyChanged;
        main.SuggestedChanges.Items.CollectionChanged += state.OnCollectionChanged;
        main.ExecutionCenter.Items.CollectionChanged += state.OnCollectionChanged;
        window.Closed += state.OnClosed;

        window.Dispatcher.BeginInvoke(new Action(state.EnsureAndRefresh));
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element) return;
        var window = Window.GetWindow(element) as MainWindow;
        if (window is null || !Attached.TryGetValue(window, out var state)) return;

        if (IsLegacyApprovedSurface(element))
        {
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = false;
            element.Focusable = false;
            Panel.SetZIndex(element, -1000);
            state.EnsureAndRefresh();
            return;
        }

        if (Equals(element.Tag, "PrimaryWorkActionBar"))
            state.EnsureAndRefresh();
    }

    private static bool IsLegacyApprovedSurface(FrameworkElement element)
    {
        if (element is not Border and not ContentControl) return false;
        if (Equals(element.Tag, "DockedExecutionNotice")) return false;

        var text = ReadText(element);
        return ApprovedMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
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
        private Border? _notice;
        private TextBlock? _message;
        private Button? _open;
        private Button? _close;
        private int _dismissedApprovedCount = -1;

        public void EnsureAndRefresh()
        {
            if (!window.IsLoaded || window.Content is not DependencyObject root) return;

            var actionBar = FindByTag<Border>(root, "PrimaryWorkActionBar");
            if (actionBar?.Child is not Grid grid) return;

            if (_notice is null || _notice.Parent is null)
            {
                _notice = BuildNotice();
                Grid.SetColumn(_notice, 1);
                Panel.SetZIndex(_notice, 50);
                grid.Children.Add(_notice);
            }

            Refresh();
        }

        private Border BuildNotice()
        {
            var shell = new Border
            {
                Tag = "DockedExecutionNotice",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 2, 6, 2),
                Padding = new Thickness(10, 2, 4, 2),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("PrimaryBrush", Brushes.MediumPurple),
                Background = Brush("SurfaceBrush", Brushes.White),
                Visibility = Visibility.Collapsed
            };

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _message = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("TextPrimaryBrush", Brushes.Black),
                Margin = new Thickness(0, 0, 8, 0)
            };
            panel.Children.Add(_message);

            _open = new Button
            {
                Content = "Open Execution Center",
                Height = 24,
                Padding = new Thickness(9, 1, 9, 1),
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Review, back up, execute and verify approved changes"
            };
            _open.Click += async (_, _) =>
            {
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                if (main.ExecutionCenter.LoadCommand.CanExecute(null))
                    await main.ExecutionCenter.LoadCommand.ExecuteAsync(null);
            };
            panel.Children.Add(_open);

            _close = new Button
            {
                Content = "✕",
                Width = 25,
                Height = 24,
                Padding = new Thickness(0),
                ToolTip = "Dismiss until the approved count changes",
                Focusable = false
            };
            _close.Click += (_, _) =>
            {
                _dismissedApprovedCount = CurrentApprovedCount();
                shell.Visibility = Visibility.Collapsed;
                shell.IsHitTestVisible = false;
            };
            panel.Children.Add(_close);

            shell.Child = panel;
            return shell;
        }

        private void Refresh()
        {
            if (_notice is null || _message is null) return;

            var approved = CurrentApprovedCount();
            var ready = main.ExecutionCenter.ReadyCount;
            var count = Math.Max(approved, ready);
            var shouldShow = count > 0 && count != _dismissedApprovedCount &&
                             !main.CurrentPage.Equals("Execution Center", StringComparison.OrdinalIgnoreCase);

            _message.Text = count == 1
                ? "1 approved change ready"
                : $"{count} approved changes ready";
            _notice.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            _notice.IsHitTestVisible = shouldShow;
        }

        private int CurrentApprovedCount() =>
            Math.Max(main.SuggestedChanges.ApprovedCount, main.ExecutionCenter.ReadyCount);

        public void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or
                nameof(MainWindowViewModel.IsOperationRunning) or
                "ApprovedCount" or "ReadyCount")
                EnsureAndRefresh();
        }

        public void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
            EnsureAndRefresh();

        public void OnClosed(object? sender, EventArgs e)
        {
            main.PropertyChanged -= OnPropertyChanged;
            main.SuggestedChanges.PropertyChanged -= OnPropertyChanged;
            main.ExecutionCenter.PropertyChanged -= OnPropertyChanged;
            main.SuggestedChanges.Items.CollectionChanged -= OnCollectionChanged;
            main.ExecutionCenter.Items.CollectionChanged -= OnCollectionChanged;
            window.Closed -= OnClosed;

            if (_notice?.Parent is Panel parent) parent.Children.Remove(_notice);
            if (_notice is not null) _notice.Child = null;
            _notice = null;
            _message = null;
            _open = null;
            _close = null;
        }
    }

    private static T? FindByTag<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        if (root is T match && Equals(match.Tag, tag)) return match;
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) return null;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var result = FindByTag<T>(child, tag);
            if (result is not null) return result;
        }
        return null;
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
