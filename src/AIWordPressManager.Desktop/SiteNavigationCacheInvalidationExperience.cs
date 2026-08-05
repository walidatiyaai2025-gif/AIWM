using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Prevents page freshness from leaking across websites. Any selected-site change
/// invalidates all recent navigation loads so the next page visit reads that site's data.
/// </summary>
internal static class SiteNavigationCacheInvalidationExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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
        if (window.DataContext is not MainWindowViewModel main) return;

        var state = new State(window, main);
        Attached.Add(window, state);
        state.Attach();
    }

    private sealed class State(MainWindow window, MainWindowViewModel main)
    {
        public void Attach()
        {
            main.Sites.PropertyChanged += OnSitesPropertyChanged;
            window.Closed += OnClosed;
        }

        private void OnSitesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or "SelectedSite")
                main.InvalidateNavigationLoadCache();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            main.Sites.PropertyChanged -= OnSitesPropertyChanged;
            window.Closed -= OnClosed;
        }
    }
}
