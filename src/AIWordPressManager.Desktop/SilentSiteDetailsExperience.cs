using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Automatic local detail hydration should not look like a foreground operation.
/// Explicit tests, deletes, saves, and connection work remain visible.
/// </summary>
internal static class SilentSiteDetailsExperience
{
    private const string AutomaticDetailsMessage = "Loading selected site details…";
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (Attached.TryGetValue(window, out _))
            return;

        if (window.DataContext is not MainWindowViewModel main)
            return;

        var state = new State(window, main.Sites);
        Attached.Add(window, state);
        state.Attach();
    }

    private sealed class State(MainWindow window, SitesViewModel sites)
    {
        private bool _disposed;
        private bool _clearing;

        public void Attach()
        {
            sites.PropertyChanged += OnSitesPropertyChanged;
            window.Closed += OnWindowClosed;
            HideAutomaticMessage();
        }

        private void OnSitesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed || _clearing || e.PropertyName != nameof(SitesViewModel.CurrentOperation))
                return;

            HideAutomaticMessage();
        }

        private void HideAutomaticMessage()
        {
            if (!string.Equals(sites.CurrentOperation, AutomaticDetailsMessage, StringComparison.Ordinal))
                return;

            _clearing = true;
            try
            {
                sites.CurrentOperation = string.Empty;
            }
            finally
            {
                _clearing = false;
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            _disposed = true;
            sites.PropertyChanged -= OnSitesPropertyChanged;
            window.Closed -= OnWindowClosed;
        }
    }
}
