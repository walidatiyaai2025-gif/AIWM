using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class SeoAuditJourneyGateCoordinator
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
                main.SeoAudit.PropertyChanged += OnAuditPropertyChanged;
                main.SeoAudit.Issues.CollectionChanged += OnAuditCollectionChanged;
                main.SeoAudit.History.CollectionChanged += OnAuditCollectionChanged;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                QueueRefresh();
            }

            private void OnAuditPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(SeoAuditViewModel.Score) or
                    nameof(SeoAuditViewModel.AuditedItems) or
                    nameof(SeoAuditViewModel.HighIssues) or
                    nameof(SeoAuditViewModel.MediumIssues) or
                    nameof(SeoAuditViewModel.LowIssues) or
                    nameof(SeoAuditViewModel.IsRunning) or
                    nameof(SeoAuditViewModel.IsFirstJourneyReady))
                {
                    QueueRefresh();
                }
            }

            private void OnAuditCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _refreshPending = false;
                    ApplyGate();
                }), DispatcherPriority.ContextIdle);
            }

            private void ApplyGate()
            {
                main.SeoAudit.RefreshFirstJourneyReadiness();
                main.RefreshFirstJourneySidebar();

                if (!main.Sites.IsFirstJourneyReady || !main.Explorer.IsFirstJourneyReady || main.SeoAudit.IsFirstJourneyReady)
                    return;

                main.ApplySeoAuditJourneyGate();
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.SeoAudit.PropertyChanged -= OnAuditPropertyChanged;
                main.SeoAudit.Issues.CollectionChanged -= OnAuditCollectionChanged;
                main.SeoAudit.History.CollectionChanged -= OnAuditCollectionChanged;
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
        internal void ApplySeoAuditJourneyGate()
        {
            if (!Sites.IsFirstJourneyReady || !Explorer.IsFirstJourneyReady || SeoAudit.IsFirstJourneyReady)
                return;

            CurrentJourneyStepTitle = "Build SEO baseline";
            CurrentJourneyStepDescription = SeoAudit.FirstJourneyStatus;
            CurrentJourneyActionLabel = "Open SEO Audit";
            CurrentJourneyTarget = "SEO Audit";
        }
    }
}
