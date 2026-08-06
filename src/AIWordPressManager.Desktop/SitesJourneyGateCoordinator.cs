using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop
{
    internal static class SitesJourneyGateCoordinator
    {
        private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded),
                true);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
                return;
            if (Attached.TryGetValue(window, out _))
                return;
            if (window.DataContext is not MainWindowViewModel main)
                return;

            var state = new State(window, main);
            Attached.Add(window, state);
            state.Attach();
        }

        private sealed class State(MainWindow window, MainWindowViewModel main)
        {
            private bool _refreshPending;

            public void Attach()
            {
                main.Sites.PropertyChanged += OnSitesPropertyChanged;
                main.Sites.Sites.CollectionChanged += OnSitesCollectionChanged;
                main.Sites.Wizard.SiteSaved += OnSiteSaved;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                QueueRefresh();
            }

            private void OnSitesPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(SitesViewModel.SelectedSite) or
                    nameof(SitesViewModel.SelectedSiteDetails) or
                    nameof(SitesViewModel.IsFirstJourneyReady) or
                    nameof(SitesViewModel.IsLoading) or
                    nameof(SitesViewModel.IsTestingConnection))
                {
                    QueueRefresh();
                }
            }

            private void OnSitesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
                => QueueRefresh();

            private void OnSiteSaved(object? sender, EventArgs e)
                => QueueRefresh();

            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.CurrentJourneyTarget) or
                    nameof(MainWindowViewModel.CurrentPage))
                {
                    QueueRefresh();
                }
            }

            private void QueueRefresh()
            {
                if (_refreshPending || window.Dispatcher.HasShutdownStarted)
                    return;

                _refreshPending = true;
                window.Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        _refreshPending = false;
                        ApplyGate();
                    }),
                    DispatcherPriority.ContextIdle);
            }

            private void ApplyGate()
            {
                main.Sites.RefreshFirstJourneyReadiness();
                main.RefreshFirstJourneySidebar();

                if (main.Sites.IsFirstJourneyReady)
                    return;

                main.ApplySitesJourneyGate();
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.Sites.PropertyChanged -= OnSitesPropertyChanged;
                main.Sites.Sites.CollectionChanged -= OnSitesCollectionChanged;
                main.Sites.Wizard.SiteSaved -= OnSiteSaved;
                main.PropertyChanged -= OnMainPropertyChanged;
                window.Closed -= OnClosed;
            }
        }
    }
}

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        internal void ApplySitesJourneyGate()
        {
            if (Sites.IsFirstJourneyReady)
                return;

            CurrentJourneyStepTitle = "Complete Sites setup";
            CurrentJourneyStepDescription = Sites.FirstJourneyStatus;
            CurrentJourneyActionLabel = "Open Sites";
            CurrentJourneyTarget = "Sites";
        }
    }
}
