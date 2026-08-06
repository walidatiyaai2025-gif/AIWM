using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

internal static class PersistentWorkspaceTabsExperience
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "Workspace", "workspace-tabs.json");

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
        if (sender is not MainWindow window || window.Content is not Grid root)
            return;

        if (root.Children.OfType<Border>().Any(x => Equals(x.Tag, "ProfessionalWorkspaceTabs")))
            return;

        var controller = new WorkspaceTabsController(window, root, StatePath);
        controller.Attach();
    }

    private sealed class WorkspaceTabsController
    {
        private readonly MainWindow _window;
        private readonly Grid _root;
        private readonly string _statePath;
        private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
        private readonly ScrollViewer _scroll = new()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        private readonly List<string> _openPages = [];
        private INotifyPropertyChanged? _notifier;
        private string _currentPage = "Dashboard";
        private bool _restoring;

        internal WorkspaceTabsController(MainWindow window, Grid root, string statePath)
        {
            _window = window;
            _root = root;
            _statePath = statePath;
        }

        internal void Attach()
        {
            _scroll.Content = _tabs;
            var border = new Border
            {
                Tag = "ProfessionalWorkspaceTabs",
                Background = ResolveBrush("SurfaceBrush", Brushes.White),
                BorderBrush = ResolveBrush("BorderBrush", Brushes.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 3, 10, 3),
                Child = _scroll
            };

            var targetRow = Math.Min(2, Math.Max(0, _root.RowDefinitions.Count - 1));
            Grid.SetRow(border, targetRow);
            Panel.SetZIndex(border, 6000);
            _root.Children.Add(border);

            LoadState();
            HookViewModel();
            _window.Closed += (_, _) => SaveState();
            _window.PreviewKeyDown += OnPreviewKeyDown;

            if (_openPages.Count == 0)
                _openPages.Add(ReadCurrentPage() ?? "Dashboard");

            _currentPage = ReadCurrentPage() ?? _openPages.LastOrDefault() ?? "Dashboard";
            EnsureOpen(_currentPage);
            RenderTabs();

            _window.Dispatcher.BeginInvoke(new Action(RestoreLastPage),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void HookViewModel()
        {
            if (_notifier is not null)
                _notifier.PropertyChanged -= OnPropertyChanged;

            _notifier = _window.DataContext as INotifyPropertyChanged;
            if (_notifier is not null)
                _notifier.PropertyChanged += OnPropertyChanged;

            _window.DataContextChanged += (_, _) => HookViewModel();
        }

        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "CurrentPage", StringComparison.Ordinal))
                return;

            var page = ReadCurrentPage();
            if (string.IsNullOrWhiteSpace(page)) return;
            _currentPage = page;
            EnsureOpen(page);
            RenderTabs();
            SaveState();
        }

        private void EnsureOpen(string page)
        {
            if (_openPages.Contains(page, StringComparer.OrdinalIgnoreCase)) return;
            _openPages.Add(page);
            while (_openPages.Count > 12)
                _openPages.RemoveAt(0);
        }

        private void RenderTabs()
        {
            _tabs.Children.Clear();
            foreach (var page in _openPages.ToArray())
            {
                var active = string.Equals(page, _currentPage, StringComparison.OrdinalIgnoreCase);
                var container = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(2),
                    Padding = new Thickness(2),
                    Background = active ? ResolveBrush("SelectionBrush", Brushes.LightBlue) : Brushes.Transparent,
                    BorderBrush = active ? ResolveBrush("PrimaryBrush", Brushes.Teal) : ResolveBrush("BorderBrush", Brushes.LightGray),
                    BorderThickness = active ? new Thickness(1.5) : new Thickness(1)
                };

                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var open = new Button
                {
                    Content = page,
                    Tag = page,
                    ToolTip = $"Open {page}",
                    Padding = new Thickness(12, 6, 8, 6),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = ResolveBrush("TextPrimaryBrush", Brushes.Black),
                    FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };
                open.Click += (_, _) => Navigate(page);

                var close = new Button
                {
                    Content = "×",
                    Tag = page,
                    ToolTip = $"Close {page}",
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(0, 2, 2, 2),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = ResolveBrush("TextSecondaryBrush", Brushes.DimGray),
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand
                };
                close.Click += (_, _) => ClosePage(page);

                panel.Children.Add(open);
                panel.Children.Add(close);
                container.Child = panel;
                _tabs.Children.Add(container);
            }
        }

        private void ClosePage(string page)
        {
            if (_openPages.Count <= 1) return;
            var index = _openPages.FindIndex(x => string.Equals(x, page, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;
            var wasCurrent = string.Equals(page, _currentPage, StringComparison.OrdinalIgnoreCase);
            _openPages.RemoveAt(index);
            if (wasCurrent)
            {
                var next = _openPages[Math.Clamp(index - 1, 0, _openPages.Count - 1)];
                Navigate(next);
            }
            RenderTabs();
            SaveState();
        }

        private void Navigate(string page)
        {
            var command = _window.DataContext?.GetType().GetProperty("NavigateCommand")?.GetValue(_window.DataContext) as ICommand;
            if (command?.CanExecute(page) == true)
                command.Execute(page);
        }

        private string? ReadCurrentPage()
            => _window.DataContext?.GetType().GetProperty("CurrentPage")?.GetValue(_window.DataContext)?.ToString();

        private void RestoreLastPage()
        {
            if (_restoring || _openPages.Count == 0) return;
            _restoring = true;
            try
            {
                var page = _currentPage;
                if (!string.IsNullOrWhiteSpace(page)) Navigate(page);
            }
            finally
            {
                _restoring = false;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.Tab && _openPages.Count > 1)
            {
                var current = _openPages.FindIndex(x => string.Equals(x, _currentPage, StringComparison.OrdinalIgnoreCase));
                var next = (current + 1) % _openPages.Count;
                Navigate(_openPages[next]);
                e.Handled = true;
            }
            else if (e.Key == Key.W && _openPages.Count > 1)
            {
                ClosePage(_currentPage);
                e.Handled = true;
            }
        }

        private void LoadState()
        {
            try
            {
                if (!File.Exists(_statePath)) return;
                var state = JsonSerializer.Deserialize<WorkspaceTabsState>(File.ReadAllText(_statePath));
                if (state is null) return;
                _openPages.AddRange(state.OpenPages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(12));
                if (!string.IsNullOrWhiteSpace(state.ActivePage))
                    _currentPage = state.ActivePage;
            }
            catch
            {
                _openPages.Clear();
            }
        }

        private void SaveState()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                var state = new WorkspaceTabsState(_openPages.ToArray(), _currentPage);
                File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Workspace persistence is optional and must never block shutdown.
            }
        }

        private static Brush ResolveBrush(string key, Brush fallback)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }

    private sealed record WorkspaceTabsState(string[] OpenPages, string ActivePage);
}
