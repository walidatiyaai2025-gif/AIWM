using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Adds a centralized navigation history, Back/Forward actions, a persistent last page,
/// and a compact breadcrumb bar without rewriting the existing MainWindow layout.
/// </summary>
internal static class ProfessionalNavigationShellExperience
{
    private static readonly ConditionalWeakTable<MainWindow, NavigationState> States = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnNavigationButtonClicked),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || States.TryGetValue(window, out _))
            return;

        var state = new NavigationState(window);
        States.Add(window, state);
        state.Attach();
    }

    private static void OnNavigationButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            !States.TryGetValue(window, out var state))
            return;

        var page = button.CommandParameter?.ToString();
        if (string.IsNullOrWhiteSpace(page))
            return;

        state.Record(page.Trim());
    }

    private sealed class NavigationState
    {
        private readonly MainWindow _window;
        private readonly List<string> _history = new();
        private int _index = -1;
        private bool _navigatingHistory;
        private Button? _backButton;
        private Button? _forwardButton;
        private TextBlock? _breadcrumb;
        private string? _lastObservedPage;
        private readonly string _statePath;

        internal NavigationState(MainWindow window)
        {
            _window = window;
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager",
                "Navigation");
            Directory.CreateDirectory(folder);
            _statePath = Path.Combine(folder, "navigation-state.json");
        }

        internal void Attach()
        {
            _window.PreviewKeyDown += OnPreviewKeyDown;
            _window.Closed += OnClosed;
            _window.DataContextChanged += OnDataContextChanged;

            AddNavigationBar();
            LoadState();
            ObserveCurrentPage();

            _window.Dispatcher.BeginInvoke(new Action(RestoreLastPage),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        internal void Record(string page)
        {
            if (_navigatingHistory || string.Equals(Current, page, StringComparison.OrdinalIgnoreCase))
            {
                UpdateVisuals(page);
                return;
            }

            if (_index < _history.Count - 1)
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);

            _history.Add(page);
            if (_history.Count > 100)
                _history.RemoveAt(0);

            _index = _history.Count - 1;
            UpdateVisuals(page);
            SaveState();
        }

        private string? Current => _index >= 0 && _index < _history.Count ? _history[_index] : null;

        private void AddNavigationBar()
        {
            if (_window.Content is not Grid root)
                return;

            var bar = new Border
            {
                Background = FindBrush("SurfaceBrush", Brushes.White),
                BorderBrush = FindBrush("BorderBrush", Brushes.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(0),
                Tag = "ProfessionalNavigationShell"
            };
            Panel.SetZIndex(bar, 8000);

            var panel = new DockPanel { LastChildFill = true };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };

            _backButton = CreateButton("←", "Back (Alt+Left)", (_, _) => NavigateBack());
            _forwardButton = CreateButton("→", "Forward (Alt+Right)", (_, _) => NavigateForward());
            actions.Children.Add(_backButton);
            actions.Children.Add(_forwardButton);
            DockPanel.SetDock(actions, Dock.Left);

            _breadcrumb = new TextBlock
            {
                Text = "Home",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextSecondaryBrush", Brushes.DimGray),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            panel.Children.Add(actions);
            panel.Children.Add(_breadcrumb);
            bar.Child = panel;

            Grid.SetRow(bar, 2);
            root.Children.Add(bar);
        }

        private static Button CreateButton(string content, string tooltip, RoutedEventHandler click)
        {
            var button = new Button
            {
                Content = content,
                ToolTip = tooltip,
                Width = 34,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(4),
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            button.SetResourceReference(Control.BackgroundProperty, "SurfaceAltBrush");
            button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            button.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            button.Click += click;
            return button;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
                return;

            if (e.Key == Key.Left)
            {
                NavigateBack();
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                NavigateForward();
                e.Handled = true;
            }
        }

        private void NavigateBack()
        {
            if (_index <= 0)
                return;

            _index--;
            NavigateToHistoryItem();
        }

        private void NavigateForward()
        {
            if (_index >= _history.Count - 1)
                return;

            _index++;
            NavigateToHistoryItem();
        }

        private void NavigateToHistoryItem()
        {
            var page = Current;
            if (string.IsNullOrWhiteSpace(page))
                return;

            _navigatingHistory = true;
            try
            {
                ExecuteNavigate(page);
                UpdateVisuals(page);
                SaveState();
            }
            finally
            {
                _window.Dispatcher.BeginInvoke(new Action(() => _navigatingHistory = false),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ExecuteNavigate(string page)
        {
            var context = _window.DataContext;
            if (context is null)
                return;

            var property = context.GetType().GetProperty("NavigateCommand", BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(context) is ICommand command && command.CanExecute(page))
                command.Execute(page);
        }

        private void RestoreLastPage()
        {
            if (_history.Count == 0 || _index < 0)
                return;

            var page = Current;
            if (!string.IsNullOrWhiteSpace(page))
            {
                ExecuteNavigate(page);
                UpdateVisuals(page);
            }
        }

        private void ObserveCurrentPage()
        {
            if (_window.DataContext is not INotifyPropertyChanged observable)
                return;

            observable.PropertyChanged -= OnViewModelPropertyChanged;
            observable.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldObservable)
                oldObservable.PropertyChanged -= OnViewModelPropertyChanged;
            ObserveCurrentPage();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, "CurrentPage", StringComparison.Ordinal))
                return;

            var page = ReadCurrentPage();
            if (string.IsNullOrWhiteSpace(page) || string.Equals(page, _lastObservedPage, StringComparison.OrdinalIgnoreCase))
                return;

            _lastObservedPage = page;
            Record(page);
        }

        private string? ReadCurrentPage()
        {
            var context = _window.DataContext;
            var property = context?.GetType().GetProperty("CurrentPage", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(context)?.ToString();
        }

        private void UpdateVisuals(string page)
        {
            _lastObservedPage = page;
            if (_breadcrumb is not null)
                _breadcrumb.Text = BuildBreadcrumb(page);

            if (_backButton is not null)
                _backButton.IsEnabled = _index > 0;
            if (_forwardButton is not null)
                _forwardButton.IsEnabled = _index >= 0 && _index < _history.Count - 1;
        }

        private static string BuildBreadcrumb(string page)
        {
            var group = page switch
            {
                "Dashboard" or "Sites" or "WordPress Explorer" => "Home",
                "Content Audit" or "SEO Audit" or "SEO History" or "Content Planner" or "Article Generator" or "Internal Links" or "Post SEO Editor" => "Content & SEO",
                "Theme Inspector" or "Visual Inspector" or "Visual WordPress Editor" or "Design Audit" or "Responsive Audit" or "Performance" or "Accessibility" or "Broken Links" => "Design & Quality",
                "AI Studio" or "AI Site Brain" or "AI Autopilot" or "AI Decision Center" or "Suggested Changes" or "Approval Queue" or "Execution Center" => "AI & Automation",
                _ => "System"
            };
            return $"{group}  ›  {page}";
        }

        private void LoadState()
        {
            try
            {
                if (!File.Exists(_statePath))
                    return;

                var json = File.ReadAllText(_statePath);
                var state = JsonSerializer.Deserialize<PersistedNavigationState>(json);
                if (state?.History is null || state.History.Count == 0)
                    return;

                _history.Clear();
                _history.AddRange(state.History.Where(x => !string.IsNullOrWhiteSpace(x)).TakeLast(100));
                _index = Math.Clamp(state.Index, 0, _history.Count - 1);
                UpdateVisuals(Current ?? "Dashboard");
            }
            catch
            {
                _history.Clear();
                _index = -1;
            }
        }

        private void SaveState()
        {
            try
            {
                var state = new PersistedNavigationState(_history, _index);
                File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Navigation must remain available even when persistence is unavailable.
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            SaveState();
            _window.PreviewKeyDown -= OnPreviewKeyDown;
            _window.Closed -= OnClosed;
            _window.DataContextChanged -= OnDataContextChanged;
            if (_window.DataContext is INotifyPropertyChanged observable)
                observable.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private static Brush FindBrush(string key, Brush fallback)
            => Application.Current.TryFindResource(key) as Brush ?? fallback;
    }

    private sealed record PersistedNavigationState(IReadOnlyList<string> History, int Index);
}
