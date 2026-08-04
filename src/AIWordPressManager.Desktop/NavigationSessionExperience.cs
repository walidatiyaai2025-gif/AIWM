using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Persists the user's last page, selected website, and recent page history between sessions.
/// The feature is deliberately independent from the in-memory Back/Forward implementation.
/// </summary>
internal static class NavigationSessionExperience
{
    private static readonly ConditionalWeakTable<MainWindow, SessionState> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);

    private static async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var host = FindTopBar(root);
        if (host is null) return;

        var state = new SessionState(main);
        Attached.Add(window, state);

        var recent = new Button
        {
            Content = "Recent ▾",
            ToolTip = "Open a recently visited page (Ctrl+Shift+R)",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26,
            Tag = "RecentPagesButton"
        };
        recent.Click += (_, _) => OpenRecentMenu(recent, state);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), recent);

        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                state.RecordPage(main.CurrentPage);
        };
        main.Sites.SelectedSiteChanged += (_, _) => state.RecordSelectedSite();

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.R ||
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;
            args.Handled = true;
            OpenRecentMenu(recent, state);
        };

        window.Closing += (_, _) => state.SaveNow();

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await state.RestoreAsync();
    }

    private static void OpenRecentMenu(Button owner, SessionState state)
    {
        var menu = new ContextMenu { PlacementTarget = owner };
        var pages = state.RecentPages;

        if (pages.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No recent pages", IsEnabled = false });
        }
        else
        {
            foreach (var page in pages)
            {
                var item = new MenuItem
                {
                    Header = page,
                    IsChecked = string.Equals(page, state.CurrentPage, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += async (_, _) => await state.NavigateToAsync(page);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var dashboard = new MenuItem { Header = "Go to Dashboard" };
        dashboard.Click += async (_, _) => await state.NavigateToAsync("Dashboard");
        menu.Items.Add(dashboard);

        var clear = new MenuItem { Header = "Clear recent pages" };
        clear.Click += (_, _) => state.ClearRecentPages();
        menu.Items.Add(clear);

        owner.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private sealed class SessionState
    {
        private const int MaximumRecentPages = 12;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly MainWindowViewModel _main;
        private readonly string _filePath;
        private readonly List<string> _recentPages = [];
        private bool _isRestoring;
        private DispatcherTimer? _saveTimer;

        public SessionState(MainWindowViewModel main)
        {
            _main = main;
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager");
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "navigation-session.json");
        }

        public IReadOnlyList<string> RecentPages => _recentPages;
        public string CurrentPage => _main.CurrentPage;

        public void RecordPage(string? page)
        {
            if (_isRestoring || string.IsNullOrWhiteSpace(page)) return;
            var normalized = page.Trim();
            _recentPages.RemoveAll(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
            _recentPages.Insert(0, normalized);
            if (_recentPages.Count > MaximumRecentPages)
                _recentPages.RemoveRange(MaximumRecentPages, _recentPages.Count - MaximumRecentPages);
            ScheduleSave();
        }

        public void RecordSelectedSite()
        {
            if (!_isRestoring) ScheduleSave();
        }

        public async Task RestoreAsync()
        {
            var snapshot = ReadSnapshot();
            if (snapshot is null) return;

            _isRestoring = true;
            try
            {
                _recentPages.Clear();
                foreach (var page in snapshot.RecentPages
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(MaximumRecentPages))
                    _recentPages.Add(page.Trim());

                if (_main.Sites.Sites.Count == 0 && _main.Sites.LoadCommand.CanExecute(null))
                    await _main.Sites.LoadCommand.ExecuteAsync(null);

                if (snapshot.SelectedSiteId is Guid siteId)
                {
                    var site = _main.Sites.Sites.FirstOrDefault(x => x.Id == siteId);
                    if (site is not null && _main.Sites.SelectSiteCommand.CanExecute(site))
                        await _main.Sites.SelectSiteCommand.ExecuteAsync(site);
                }

                var target = string.IsNullOrWhiteSpace(snapshot.LastPage) ? "Sites" : snapshot.LastPage.Trim();
                if (_main.NavigateCommand.CanExecute(target))
                    await _main.NavigateCommand.ExecuteAsync(target);
            }
            catch
            {
                // A stale or partially incompatible session must never prevent application startup.
            }
            finally
            {
                _isRestoring = false;
                RecordPage(_main.CurrentPage);
            }
        }

        public async Task NavigateToAsync(string page)
        {
            if (string.IsNullOrWhiteSpace(page) || !_main.NavigateCommand.CanExecute(page)) return;
            await _main.NavigateCommand.ExecuteAsync(page);
        }

        public void ClearRecentPages()
        {
            _recentPages.Clear();
            RecordPage(_main.CurrentPage);
            SaveNow();
        }

        public void SaveNow()
        {
            try
            {
                _saveTimer?.Stop();
                var snapshot = new NavigationSessionSnapshot(
                    _main.CurrentPage,
                    _main.Sites.SelectedSite?.Id,
                    _recentPages.ToArray(),
                    DateTime.UtcNow);
                var temporary = _filePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
                File.Move(temporary, _filePath, true);
            }
            catch
            {
                // Session persistence is optional and must never interrupt normal work.
            }
        }

        private void ScheduleSave()
        {
            _saveTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveTimer.Stop();
            _saveTimer.Tick -= SaveTimerOnTick;
            _saveTimer.Tick += SaveTimerOnTick;
            _saveTimer.Start();
        }

        private void SaveTimerOnTick(object? sender, EventArgs e)
        {
            _saveTimer?.Stop();
            SaveNow();
        }

        private NavigationSessionSnapshot? ReadSnapshot()
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                return JsonSerializer.Deserialize<NavigationSessionSnapshot>(File.ReadAllText(_filePath));
            }
            catch
            {
                return null;
            }
        }
    }

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
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private sealed record NavigationSessionSnapshot(
        string LastPage,
        Guid? SelectedSiteId,
        string[] RecentPages,
        DateTime SavedAtUtc);
}
