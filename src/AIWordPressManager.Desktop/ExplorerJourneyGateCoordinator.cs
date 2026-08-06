using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class ExplorerJourneyGateCoordinator
    {
        private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded), true);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
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
            private bool _refreshPending;

            public void Attach()
            {
                main.Explorer.PropertyChanged += OnExplorerPropertyChanged;
                main.Sites.PropertyChanged += OnSitesPropertyChanged;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                QueueRefresh();
            }

            private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(WordPressExplorerViewModel.LoadedAt) or
                    nameof(WordPressExplorerViewModel.TotalPosts) or
                    nameof(WordPressExplorerViewModel.TotalPages) or
                    nameof(WordPressExplorerViewModel.TotalCategories) or
                    nameof(WordPressExplorerViewModel.TotalTags) or
                    nameof(WordPressExplorerViewModel.TotalMedia) or
                    nameof(WordPressExplorerViewModel.IsLoading) or
                    nameof(WordPressExplorerViewModel.ProgressPercent) or
                    nameof(WordPressExplorerViewModel.CurrentOperation) or
                    nameof(WordPressExplorerViewModel.IsFirstJourneyReady))
                {
                    QueueRefresh();
                }
            }

            private void OnSitesPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is "IsFirstJourneyReady" or "SelectedSite") QueueRefresh();
            }

            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.CurrentJourneyTarget) or
                    nameof(MainWindowViewModel.CurrentPage)) QueueRefresh();
            }

            private void QueueRefresh()
            {
                if (_refreshPending || window.Dispatcher.HasShutdownStarted) return;
                _refreshPending = true;
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _refreshPending = false;
                    ApplyGate();
                }), DispatcherPriority.ContextIdle);
            }

            private void ApplyGate()
            {
                main.Explorer.RefreshFirstJourneyReadiness();
                main.RefreshFirstJourneySidebar();

                if (!main.Sites.IsFirstJourneyReady || main.Explorer.IsFirstJourneyReady) return;
                main.ApplyExplorerJourneyGate();
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.Explorer.PropertyChanged -= OnExplorerPropertyChanged;
                main.Sites.PropertyChanged -= OnSitesPropertyChanged;
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
        internal void ApplyExplorerJourneyGate()
        {
            if (!Sites.IsFirstJourneyReady || Explorer.IsFirstJourneyReady) return;
            CurrentJourneyStepTitle = "Complete WordPress snapshot";
            CurrentJourneyStepDescription = Explorer.FirstJourneyStatus;
            CurrentJourneyActionLabel = "Open WordPress Explorer";
            CurrentJourneyTarget = "WordPress Explorer";
        }
    }
}
