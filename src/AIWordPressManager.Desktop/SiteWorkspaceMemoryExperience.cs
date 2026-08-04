using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class SiteWorkspaceMemoryExperience
{
    private static readonly ConditionalWeakTable<MainWindow, WorkspaceState> Attached = new();

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

        var state = new WorkspaceState(main);
        Attached.Add(window, state);

        var switcher = new Button
        {
            Content = "Site ▾",
            ToolTip = "Switch active WordPress site and restore its last workspace (Ctrl+Shift+S)",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 26,
            Tag = "SiteWorkspaceSwitcher"
        };
        switcher.Click += (_, _) => OpenSiteMenu(switcher, state);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), switcher);

        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                state.RecordCurrentPage();
        };
        main.Sites.SelectedSiteChanged += async (_, _) => await state.OnSelectedSiteChangedAsync();
        window.Closing += (_, _) => state.SaveNow();

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.S ||
                (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;
            args.Handled = true;
            OpenSiteMenu(switcher, state);
        };
    }

    private static void OpenSiteMenu(Button owner, WorkspaceState state)
    {
        var menu = new ContextMenu { PlacementTarget = owner };
        var sites = state.Sites;

        if (sites.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No websites available", IsEnabled = false });
        }
        else
        {
            foreach (var site in sites)
            {
                var target = site;
                var item = new MenuItem
                {
                    Header = $"{site.Name}   [{site.DisplayHost}]",
                    IsCheckable = true,
                    IsChecked = state.SelectedSiteId == site.Id,
                    ToolTip = state.GetWorkspaceHint(site.Id)
                };
                item.Click += async (_, _) => await state.SwitchToAsync(target);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var sitesPage = new MenuItem { Header = "Manage websites" };
        sitesPage.Click += async (_, _) => await state.NavigateAsync("Sites");
        menu.Items.Add(sitesPage);

        var clear = new MenuItem { Header = "Clear saved site workspaces" };
        clear.Click += (_, _) => state.ClearWorkspaceMemory();
        menu.Items.Add(clear);

        owner.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private sealed class WorkspaceState
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly MainWindowViewModel _main;
        private readonly string _filePath;
        private readonly Dictionary<Guid, string> _lastPages;
        private Guid? _previousSiteId;
        private bool _switching;

        public WorkspaceState(MainWindowViewModel main)
        {
            _main = main;
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIWordPressManager");
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "site-workspaces.json");
            _lastPages = Load();
            _previousSiteId = _main.Sites.SelectedSite?.Id;
        }

        public IReadOnlyList<SiteCardViewModel> Sites => _main.Sites.Sites;
        public Guid? SelectedSiteId => _main.Sites.SelectedSite?.Id;

        public void RecordCurrentPage()
        {
            var siteId = _main.Sites.SelectedSite?.Id;
            var page = _main.CurrentPage;
            if (_switching || siteId is null || string.IsNullOrWhiteSpace(page)) return;
            if (page.Equals("Sites", StringComparison.OrdinalIgnoreCase)) return;
            _lastPages[siteId.Value] = page.Trim();
            SaveNow();
        }

        public async Task OnSelectedSiteChangedAsync()
        {
            var currentId = _main.Sites.SelectedSite?.Id;
            if (_previousSiteId == currentId) return;

            if (_previousSiteId is Guid previous && !string.IsNullOrWhiteSpace(_main.CurrentPage) &&
                !_main.CurrentPage.Equals("Sites", StringComparison.OrdinalIgnoreCase))
                _lastPages[previous] = _main.CurrentPage;

            _previousSiteId = currentId;
            SaveNow();

            if (_switching || currentId is null) return;
            if (_lastPages.TryGetValue(currentId.Value, out var target) &&
                !string.IsNullOrWhiteSpace(target) && _main.NavigateCommand.CanExecute(target))
            {
                _switching = true;
                try { await _main.NavigateCommand.ExecuteAsync(target); }
                finally { _switching = false; }
            }
        }

        public async Task SwitchToAsync(SiteCardViewModel site)
        {
            if (_main.Sites.SelectedSite?.Id == site.Id) return;

            var oldId = _main.Sites.SelectedSite?.Id;
            if (oldId is Guid current && !string.IsNullOrWhiteSpace(_main.CurrentPage) &&
                !_main.CurrentPage.Equals("Sites", StringComparison.OrdinalIgnoreCase))
                _lastPages[current] = _main.CurrentPage;

            _switching = true;
            try
            {
                if (_main.Sites.SelectSiteCommand.CanExecute(site))
                    await _main.Sites.SelectSiteCommand.ExecuteAsync(site);

                var target = _lastPages.TryGetValue(site.Id, out var saved) && !string.IsNullOrWhiteSpace(saved)
                    ? saved
                    : "Dashboard";

                if (_main.NavigateCommand.CanExecute(target))
                    await _main.NavigateCommand.ExecuteAsync(target);
            }
            finally
            {
                _previousSiteId = site.Id;
                _switching = false;
                SaveNow();
            }
        }

        public string GetWorkspaceHint(Guid siteId) =>
            _lastPages.TryGetValue(siteId, out var page) && !string.IsNullOrWhiteSpace(page)
                ? $"Last workspace: {page}"
                : "No saved workspace yet";

        public Task NavigateAsync(string page) =>
            _main.NavigateCommand.CanExecute(page) ? _main.NavigateCommand.ExecuteAsync(page) : Task.CompletedTask;

        public void ClearWorkspaceMemory()
        {
            _lastPages.Clear();
            SaveNow();
        }

        public void SaveNow()
        {
            try
            {
                var temp = _filePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_lastPages, JsonOptions));
                File.Move(temp, _filePath, true);
            }
            catch { }
        }

        private Dictionary<Guid, string> Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return [];
                return JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(_filePath)) ?? [];
            }
            catch { return []; }
        }
    }

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
}
