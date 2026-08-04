using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Browser-style navigation with page-state restoration. Each history entry keeps the
/// page, selected website, filters, selected row identity, and scroll position.
/// </summary>
internal static class NavigationHistoryExperience
{
    private static readonly ConditionalWeakTable<MainWindow, NavigationState> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var host = FindTopBar(root);
        if (host is null) return;

        var state = new NavigationState(window, root, main, main.CurrentPage);
        Attached.Add(window, state);

        var back = NavigationButton("←", "Back to the previous page (Alt+Left)");
        var forward = NavigationButton("→", "Forward to the next page (Alt+Right)");
        back.Tag = "GlobalNavigationBack";
        forward.Tag = "GlobalNavigationForward";
        back.Click += async (_, _) => await state.GoBackAsync();
        forward.Click += async (_, _) => await state.GoForwardAsync();
        host.Children.Insert(0, forward);
        host.Children.Insert(0, back);

        void RefreshButtons()
        {
            back.IsEnabled = state.CanGoBack;
            forward.IsEnabled = state.CanGoForward;
            back.ToolTip = state.CanGoBack ? $"Back to {state.BackTarget} (Alt+Left)" : "No previous page";
            forward.ToolTip = state.CanGoForward ? $"Forward to {state.ForwardTarget} (Alt+Right)" : "No forward page";
        }

        state.Changed += (_, _) => RefreshButtons();
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                state.RecordNavigation(main.CurrentPage);
        };

        window.PreviewKeyDown += async (_, args) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
            if (args.Key is Key.Left or Key.BrowserBack)
            {
                if (!state.CanGoBack) return;
                args.Handled = true;
                await state.GoBackAsync();
            }
            else if (args.Key is Key.Right or Key.BrowserForward)
            {
                if (!state.CanGoForward) return;
                args.Handled = true;
                await state.GoForwardAsync();
            }
        };

        window.PreviewMouseDown += async (_, args) =>
        {
            if (args.ChangedButton == MouseButton.XButton1 && state.CanGoBack)
            {
                args.Handled = true;
                await state.GoBackAsync();
            }
            else if (args.ChangedButton == MouseButton.XButton2 && state.CanGoForward)
            {
                args.Handled = true;
                await state.GoForwardAsync();
            }
        };

        window.Closing += (_, _) => state.CaptureCurrentEntry();
        RefreshButtons();
    }

    private sealed class NavigationState(Window window, Grid root, MainWindowViewModel main, string initialPage)
    {
        private const int MaximumHistory = 50;
        private readonly List<HistoryEntry> _history = [new(Normalize(initialPage), new PageSnapshot())];
        private int _index;
        private bool _isHistoryNavigation;

        public event EventHandler? Changed;
        public bool CanGoBack => _index > 0;
        public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;
        public string? BackTarget => CanGoBack ? _history[_index - 1].Page : null;
        public string? ForwardTarget => CanGoForward ? _history[_index + 1].Page : null;

        public void CaptureCurrentEntry()
        {
            if (_index < 0 || _index >= _history.Count) return;
            _history[_index] = _history[_index] with { Snapshot = CaptureSnapshot(_history[_index].Page) };
        }

        public void RecordNavigation(string? page)
        {
            var normalized = Normalize(page);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            if (_isHistoryNavigation)
            {
                _isHistoryNavigation = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_index >= 0 && _index < _history.Count &&
                string.Equals(_history[_index].Page, normalized, StringComparison.OrdinalIgnoreCase)) return;

            CaptureCurrentEntry();
            if (_index < _history.Count - 1)
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);

            _history.Add(new HistoryEntry(normalized, CaptureSnapshot(normalized)));
            _index = _history.Count - 1;
            if (_history.Count > MaximumHistory)
            {
                var removeCount = _history.Count - MaximumHistory;
                _history.RemoveRange(0, removeCount);
                _index = Math.Max(0, _index - removeCount);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task GoBackAsync() => NavigateHistoryAsync(_index - 1);
        public Task GoForwardAsync() => NavigateHistoryAsync(_index + 1);

        private async Task NavigateHistoryAsync(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _history.Count || targetIndex == _index) return;
            var target = _history[targetIndex];
            if (!main.NavigateCommand.CanExecute(target.Page)) return;

            CaptureCurrentEntry();
            var previousIndex = _index;
            _index = targetIndex;
            _isHistoryNavigation = true;
            Changed?.Invoke(this, EventArgs.Empty);

            try
            {
                await main.NavigateCommand.ExecuteAsync(target.Page);
                if (!string.Equals(main.CurrentPage, target.Page, StringComparison.OrdinalIgnoreCase))
                {
                    _index = previousIndex;
                    _isHistoryNavigation = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                    return;
                }

                await window.Dispatcher.InvokeAsync(() => RestoreSnapshot(target.Snapshot), DispatcherPriority.ContextIdle);
                await window.Dispatcher.InvokeAsync(() => RestoreScroll(target.Snapshot), DispatcherPriority.ApplicationIdle);
            }
            catch
            {
                _index = previousIndex;
                _isHistoryNavigation = false;
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        private PageSnapshot CaptureSnapshot(string page)
        {
            var snapshot = new PageSnapshot
            {
                SelectedSiteId = ReadIdentity(main.Sites.SelectedSite),
                HorizontalOffset = FindVisibleScrollViewer(root)?.HorizontalOffset ?? 0,
                VerticalOffset = FindVisibleScrollViewer(root)?.VerticalOffset ?? 0
            };

            var vm = ResolvePageViewModel(page);
            if (vm is null) return snapshot;
            foreach (var name in StatePropertyNames)
            {
                var property = vm.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (property is null || !property.CanRead || !property.CanWrite) continue;
                var value = property.GetValue(vm);
                if (IsSafeStateValue(value)) snapshot.Values[name] = value;
            }

            var selected = vm.GetType().GetProperty("SelectedItem", BindingFlags.Instance | BindingFlags.Public)?.GetValue(vm);
            snapshot.SelectedItemId = ReadIdentity(selected);
            return snapshot;
        }

        private void RestoreSnapshot(PageSnapshot snapshot)
        {
            RestoreSelectedSite(snapshot.SelectedSiteId);
            var vm = ResolvePageViewModel(main.CurrentPage);
            if (vm is null) return;

            foreach (var pair in snapshot.Values)
            {
                var property = vm.GetType().GetProperty(pair.Key, BindingFlags.Instance | BindingFlags.Public);
                if (property is null || !property.CanWrite) continue;
                try { property.SetValue(vm, pair.Value); } catch { }
            }
            RestoreSelectedItem(vm, snapshot.SelectedItemId);
        }

        private void RestoreSelectedSite(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var site = main.Sites.Sites.FirstOrDefault(x => ReadIdentity(x) == id);
            if (site is not null) main.Sites.SelectedSite = site;
        }

        private static void RestoreSelectedItem(object vm, string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var selectedProperty = vm.GetType().GetProperty("SelectedItem", BindingFlags.Instance | BindingFlags.Public);
            if (selectedProperty is null || !selectedProperty.CanWrite) return;

            foreach (var collectionName in new[] { "Items", "FilteredItems", "Sites", "FilteredSites", "Rows" })
            {
                if (vm.GetType().GetProperty(collectionName)?.GetValue(vm) is not IEnumerable collection) continue;
                foreach (var item in collection)
                {
                    if (ReadIdentity(item) != id) continue;
                    try { selectedProperty.SetValue(vm, item); } catch { }
                    return;
                }
            }
        }

        private void RestoreScroll(PageSnapshot snapshot)
        {
            var viewer = FindVisibleScrollViewer(root);
            viewer?.ScrollToHorizontalOffset(snapshot.HorizontalOffset);
            viewer?.ScrollToVerticalOffset(snapshot.VerticalOffset);
        }

        private object? ResolvePageViewModel(string page) => page switch
        {
            "Sites" => main.Sites,
            "WordPress Explorer" => main.Explorer,
            "SEO Audit" or "SEO History" => main.SeoAudit,
            "Suggested Changes" or "Approval Queue" => main.SuggestedChanges,
            "Execution Center" => main.ExecutionCenter,
            "Backups" => main.Backups,
            "Health Center" => main.HealthCenter,
            "Transaction Center" => main.TransactionCenter,
            "Evidence Center" => main.EvidenceCenter,
            "Jobs" => main.Jobs,
            "Reports" => main.Reports,
            "Logs" => main.Logs,
            "Settings" => main.Settings,
            _ => null
        };
    }

    private static readonly string[] StatePropertyNames =
    [
        "SearchText", "StatusFilter", "SelectedStatusFilter", "SelectedFilter", "SelectedType",
        "SelectedTab", "SelectedContentType", "SelectedCategory", "SelectedSeverity", "PageSize",
        "CurrentPageNumber", "SortColumn", "SortDirection"
    ];

    private static bool IsSafeStateValue(object? value) => value is null or string or bool or int or long or double or decimal or Enum or Guid;

    private static string? ReadIdentity(object? value)
    {
        if (value is null) return null;
        foreach (var name in new[] { "Id", "ChangeId", "SiteId", "TransactionId", "ObjectId" })
        {
            var property = value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var id = property?.GetValue(value);
            if (id is not null) return Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    private static ScrollViewer? FindVisibleScrollViewer(DependencyObject root)
    {
        foreach (var viewer in Enumerate<ScrollViewer>(root))
            if (viewer.IsVisible && viewer.ActualHeight > 0) return viewer;
        return null;
    }

    private static string Normalize(string? page) => string.IsNullOrWhiteSpace(page) ? "Dashboard" : page.Trim();
    private static Button NavigationButton(string content, string tooltip) => new()
    {
        Content = content, ToolTip = tooltip, Width = 34, Height = 28, Margin = new Thickness(0, 0, 5, 0),
        Padding = new Thickness(0), FontSize = 17, FontWeight = FontWeights.Bold,
        VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            if (panel.Children.OfType<FrameworkElement>().SelectMany(Enumerate<TextBlock>)
                .Any(x => x.Text?.Contains("Active:", StringComparison.OrdinalIgnoreCase) == true)) return panel;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private sealed record HistoryEntry(string Page, PageSnapshot Snapshot);
    private sealed class PageSnapshot
    {
        public string? SelectedSiteId { get; set; }
        public string? SelectedItemId { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);
    }
}
