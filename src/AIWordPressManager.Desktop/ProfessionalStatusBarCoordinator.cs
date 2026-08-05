using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps the footer stable and readable. Background services may update
/// ApplicationDataStatus several times during one operation; the footer only
/// presents the latest settled message, while warnings remain immediate.
/// </summary>
internal static class ProfessionalStatusBarCoordinator
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            handledEventsToo: true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (Attached.TryGetValue(window, out _))
            return;

        if (window.DataContext is not MainWindowViewModel viewModel)
            return;

        var statusText = FindStatusText(window);
        if (statusText is null)
            return;

        var state = new State(window, viewModel, statusText);
        Attached.Add(window, state);
        state.Attach();
    }

    private static TextBlock? FindStatusText(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBlock text)
            {
                var binding = BindingOperations.GetBinding(text, TextBlock.TextProperty);
                if (string.Equals(binding?.Path?.Path, nameof(MainWindowViewModel.ApplicationDataStatus), StringComparison.Ordinal))
                    return text;
            }

            var nested = FindStatusText(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private sealed class State(MainWindow window, MainWindowViewModel viewModel, TextBlock statusText)
    {
        private readonly DispatcherTimer _settleTimer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };

        private string _pending = string.Empty;
        private string _displayed = string.Empty;
        private bool _disposed;

        public void Attach()
        {
            BindingOperations.ClearBinding(statusText, TextBlock.TextProperty);
            statusText.MaxWidth = 760;
            statusText.TextTrimming = TextTrimming.CharacterEllipsis;
            statusText.ToolTip = "Latest application status";

            _settleTimer.Tick += OnSettleTimerTick;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            window.Closed += OnWindowClosed;

            Queue(viewModel.ApplicationDataStatus, immediate: true);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed || e.PropertyName != nameof(MainWindowViewModel.ApplicationDataStatus))
                return;

            var message = viewModel.ApplicationDataStatus;
            Queue(message, IsImportant(message));
        }

        private void Queue(string? message, bool immediate)
        {
            var normalized = Normalize(message);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, _displayed, StringComparison.Ordinal))
                return;

            _pending = normalized;
            _settleTimer.Stop();

            if (immediate)
            {
                Publish();
                return;
            }

            _settleTimer.Start();
        }

        private void OnSettleTimerTick(object? sender, EventArgs e)
        {
            _settleTimer.Stop();
            Publish();
        }

        private void Publish()
        {
            if (_disposed || string.IsNullOrWhiteSpace(_pending))
                return;

            _displayed = _pending;
            statusText.Text = _displayed;
            statusText.ToolTip = _displayed;
        }

        private static bool IsImportant(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.Contains("error", StringComparison.OrdinalIgnoreCase)
                || message.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || message.Contains("warning", StringComparison.OrdinalIgnoreCase)
                || message.Contains("could not", StringComparison.OrdinalIgnoreCase)
                || message.Contains("attention", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "Ready";

            var value = string.Join(' ', message
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            value = value
                .Replace("The active workspace will refresh in the background.", "Workspace ready.", StringComparison.OrdinalIgnoreCase)
                .Replace("The current workspace will refresh in the background.", "Workspace ready.", StringComparison.OrdinalIgnoreCase)
                .Replace("Background modules load when opened.", "Modules load when opened.", StringComparison.OrdinalIgnoreCase)
                .Replace("Reading the latest saved data from SQLite", "Loading saved data", StringComparison.OrdinalIgnoreCase);

            return value.Length <= 180 ? value : value[..177] + "…";
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            _settleTimer.Stop();
            _settleTimer.Tick -= OnSettleTimerTick;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            window.Closed -= OnWindowClosed;
        }
    }
}
