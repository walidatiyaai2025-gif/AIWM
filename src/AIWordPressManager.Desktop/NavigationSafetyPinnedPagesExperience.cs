using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Adds two application-wide navigation capabilities:
/// 1) warns before leaving a page that currently owns a critical operation;
/// 2) persists a small list of pinned pages for fast access.
/// No floating panels are created; all UI is hosted in the existing top bar.
/// </summary>
internal static class NavigationSafetyPinnedPagesExperience
{
    private static readonly ConditionalWeakTable<MainWindow, ExperienceState> Attached = new();

    private static readonly IReadOnlyDictionary<string, string> PageViewModels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sites"] = nameof(MainWindowViewModel.Sites),
            ["WordPress Explorer"] = nameof(MainWindowViewModel.Explorer),
            ["SEO Audit"] = nameof(MainWindowViewModel.SeoAudit),
            ["Suggested Changes"] = nameof(MainWindowViewModel.SuggestedChanges),
            ["Approval Queue"] = nameof(MainWindowViewModel.SuggestedChanges),
            ["Execution Center"] = nameof(MainWindowViewModel.ExecutionCenter),
            ["Backups"] = nameof(MainWindowViewModel.Backups),
            ["Health Center"] = nameof(MainWindowViewModel.HealthCenter),
            ["Transaction Center"] = nameof(MainWindowViewModel.TransactionCenter),
            ["Evidence Center"] = nameof(MainWindowViewModel.EvidenceCenter),
            ["Jobs"] = nameof(MainWindowViewModel.Jobs),
            ["Reports"] = nameof(MainWindowViewModel.Reports),
            ["Logs"] = nameof(MainWindowViewModel.Logs)
        };

    private static readonly string[] BusyPropertyNames =
    [
        "IsBusy", "IsLoading", "IsTestingConnection", "IsRefreshing", "IsSynchronizing",
        "IsExecuting", "IsRestoring", "IsSaving", "IsRunning", "IsWorking",
        "HasActiveOperation", "IsGlobalOperationActive"
    ];

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

        var state = new ExperienceState(window, main);
        Attached.Add(window, state);

        var pin = HeaderButton("☆ Pin", "Pin or unpin the current page");
        pin.Tag = "PinCurrentPageButton";
        pin.Click += (_, _) =>
        {
            state.ToggleCurrentPagePin();
            RefreshPinButton(pin, state);
        };

        var pinned = HeaderButton("Pinned ▾", "Open a pinned page");
        pinned.Tag = "PinnedPagesButton";
        pinned.Click += (_, _) => OpenPinnedMenu(pinned, state, pin);

        host.Children.Insert(Math.Max(0, host.Children.Count - 1), pin);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), pinned);

        main.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName != nameof(MainWindowViewModel.CurrentPage)) return;
            RefreshPinButton(pin, state);
            await state.OnPageChangedAsync();
        };

        window.Closing += state.OnWindowClosing;

        window.PreviewKeyDown += (_, args) =>
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) !=
                (ModifierKeys.Control | ModifierKeys.Shift)) return;

            if (args.Key == Key.P)
            {
                args.Handled = true;
                state.ToggleCurrentPagePin();
                RefreshPinButton(pin, state);
            }
            else if (args.Key == Key.O)
            {
                args.Handled = true;
                OpenPinnedMenu(pinned, state, pin);
            }
        };

        RefreshPinButton(pin, state);
    }

    private static void OpenPinnedMenu(Button owner, ExperienceState state, Button pinButton)
    {
        var menu = new ContextMenu { PlacementTarget = owner };
        if (state.PinnedPages.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No pinned pages", IsEnabled = false });
        }
        else
        {
            foreach (var page in state.PinnedPages)
            {
                var item = new MenuItem
                {
                    Header = page,
                    IsChecked = string.Equals(page, state.CurrentPage, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += async (_, _) => await state.NavigateAsync(page);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var toggle = new MenuItem
        {
            Header = state.IsCurrentPagePinned ? "Unpin current page" : "Pin current page"
        };
        toggle.Click += (_, _) =>
        {
            state.ToggleCurrentPagePin();
            RefreshPinButton(pinButton, state);
        };
        menu.Items.Add(toggle);

        var clear = new MenuItem
        {
            Header = "Clear pinned pages",
            IsEnabled = state.PinnedPages.Count > 0
        };
        clear.Click += (_, _) =>
        {
            state.ClearPins();
            RefreshPinButton(pinButton, state);
        };
        menu.Items.Add(clear);

        owner.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static void RefreshPinButton(Button button, ExperienceState state)
    {
        button.Content = state.IsCurrentPagePinned ? "★ Pinned" : "☆ Pin";
        button.ToolTip = state.IsCurrentPagePinned
            ? $"Unpin {state.CurrentPage} (Ctrl+Shift+P)"
            : $"Pin {state.CurrentPage} (Ctrl+Shift+P)";
    }

    private sealed class ExperienceState
    {
        private const int MaximumPinnedPages = 10;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly MainWindow _window;
        private readonly MainWindowViewModel _main;
        private readonly string _filePath;
        private readonly List<string> _pinnedPages;
        private string _lastStablePage;
        private bool _isGuardNavigation;
        private bool _isPromptOpen;

        public ExperienceState(MainWindow window, MainWindowViewModel main)
        {
            _window = window;
            _main = main;
            _lastStablePage = Normalize(main.CurrentPage);

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager");
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "pinned-pages.json");
            _pinnedPages = LoadPins(_filePath);
        }

        public IReadOnlyList<string> PinnedPages => _pinnedPages;
        public string CurrentPage => Normalize(_main.CurrentPage);
        public bool IsCurrentPagePinned => _pinnedPages.Any(x =>
            string.Equals(x, CurrentPage, StringComparison.OrdinalIgnoreCase));

        public async Task OnPageChangedAsync()
        {
            var current = CurrentPage;
            if (_isGuardNavigation)
            {
                _isGuardNavigation = false;
                _lastStablePage = current;
                return;
            }

            var previous = _lastStablePage;
            if (string.Equals(previous, current, StringComparison.OrdinalIgnoreCase)) return;

            if (!_isPromptOpen && IsPageBusy(previous, out var reason))
            {
                _isPromptOpen = true;
                try
                {
                    var result = MessageBox.Show(
                        _window,
                        $"{previous} still has an active operation ({reason}).\n\nLeaving the page does not cancel the operation, but it may hide its progress. Continue to {current}?",
                        "Operation still running",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                    if (result == MessageBoxResult.No && _main.NavigateCommand.CanExecute(previous))
                    {
                        _isGuardNavigation = true;
                        await _main.NavigateCommand.ExecuteAsync(previous);
                        return;
                    }
                }
                finally
                {
                    _isPromptOpen = false;
                }
            }

            _lastStablePage = current;
        }

        public async Task NavigateAsync(string page)
        {
            if (string.IsNullOrWhiteSpace(page) || !_main.NavigateCommand.CanExecute(page)) return;
            await _main.NavigateCommand.ExecuteAsync(page);
        }

        public void ToggleCurrentPagePin()
        {
            var page = CurrentPage;
            var existing = _pinnedPages.FindIndex(x =>
                string.Equals(x, page, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                _pinnedPages.RemoveAt(existing);
            }
            else
            {
                _pinnedPages.Insert(0, page);
                if (_pinnedPages.Count > MaximumPinnedPages)
                    _pinnedPages.RemoveRange(MaximumPinnedPages, _pinnedPages.Count - MaximumPinnedPages);
            }
            SavePins();
        }

        public void ClearPins()
        {
            _pinnedPages.Clear();
            SavePins();
        }

        public void OnWindowClosing(object? sender, CancelEventArgs args)
        {
            if (_isPromptOpen) return;
            var busyPages = PageViewModels.Keys
                .Where(page => IsPageBusy(page, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (busyPages.Length == 0) return;

            var result = MessageBox.Show(
                _window,
                $"Active operations were detected in:\n\n{string.Join("\n", busyPages.Select(x => "• " + x))}\n\nClosing now may interrupt local progress tracking. Close anyway?",
                "Active operations",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) args.Cancel = true;
        }

        private bool IsPageBusy(string page, out string reason)
        {
            reason = "active operation";
            if (!PageViewModels.TryGetValue(page, out var propertyName)) return false;

            var vmProperty = typeof(MainWindowViewModel).GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            var viewModel = vmProperty?.GetValue(_main);
            if (viewModel is null) return false;

            foreach (var property in BusyPropertyNames)
            {
                var busyProperty = viewModel.GetType().GetProperty(
                    property,
                    BindingFlags.Instance | BindingFlags.Public);
                if (busyProperty?.PropertyType != typeof(bool)) continue;
                try
                {
                    if (busyProperty.GetValue(viewModel) is true)
                    {
                        reason = property;
                        return true;
                    }
                }
                catch
                {
                    // A diagnostic guard must never prevent navigation because reflection failed.
                }
            }
            return false;
        }

        private void SavePins()
        {
            try
            {
                var temporary = _filePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_pinnedPages, JsonOptions));
                File.Move(temporary, _filePath, true);
            }
            catch
            {
                // Pin persistence is optional and must not interrupt the application.
            }
        }

        private static List<string> LoadPins(string path)
        {
            try
            {
                if (!File.Exists(path)) return [];
                return (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Normalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumPinnedPages)
                    .ToList();
            }
            catch
            {
                return [];
            }
        }
    }

    private static string Normalize(string? page) =>
        string.IsNullOrWhiteSpace(page) ? "Dashboard" : page.Trim();

    private static Button HeaderButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Margin = new Thickness(5, 0, 0, 0),
        Padding = new Thickness(10, 4, 10, 4),
        MinHeight = 26
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
