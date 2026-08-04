using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Adds browser-style navigation history to every application page without requiring
/// individual screens to implement their own Back behavior.
/// </summary>
internal static class NavigationHistoryExperience
{
    private static readonly ConditionalWeakTable<MainWindow, NavigationState> Attached = new();

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

        var host = FindTopBar(root);
        if (host is null) return;

        var state = new NavigationState(main, main.CurrentPage);
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
            back.ToolTip = state.CanGoBack
                ? $"Back to {state.BackTarget} (Alt+Left)"
                : "No previous page";
            forward.ToolTip = state.CanGoForward
                ? $"Forward to {state.ForwardTarget} (Alt+Right)"
                : "No forward page";
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

        RefreshButtons();
    }

    private sealed class NavigationState(MainWindowViewModel main, string initialPage)
    {
        private const int MaximumHistory = 50;
        private readonly List<string> _history = [Normalize(initialPage)];
        private int _index;
        private bool _isHistoryNavigation;

        public event EventHandler? Changed;

        public bool CanGoBack => _index > 0;
        public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;
        public string? BackTarget => CanGoBack ? _history[_index - 1] : null;
        public string? ForwardTarget => CanGoForward ? _history[_index + 1] : null;

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
                string.Equals(_history[_index], normalized, StringComparison.OrdinalIgnoreCase))
                return;

            if (_index < _history.Count - 1)
                _history.RemoveRange(_index + 1, _history.Count - _index - 1);

            _history.Add(normalized);
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
            if (!main.NavigateCommand.CanExecute(target)) return;

            var previousIndex = _index;
            _index = targetIndex;
            _isHistoryNavigation = true;
            Changed?.Invoke(this, EventArgs.Empty);

            try
            {
                await main.NavigateCommand.ExecuteAsync(target);

                if (!string.Equals(main.CurrentPage, target, StringComparison.OrdinalIgnoreCase))
                {
                    _index = previousIndex;
                    _isHistoryNavigation = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch
            {
                _index = previousIndex;
                _isHistoryNavigation = false;
                Changed?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        private static string Normalize(string? page) => string.IsNullOrWhiteSpace(page) ? "Dashboard" : page.Trim();
    }

    private static Button NavigationButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Width = 34,
        Height = 28,
        Margin = new Thickness(0, 0, 5, 0),
        Padding = new Thickness(0),
        FontSize = 17,
        FontWeight = FontWeights.Bold,
        VerticalContentAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

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
}
