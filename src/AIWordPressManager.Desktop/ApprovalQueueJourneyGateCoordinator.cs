using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class ApprovalQueueJourneyGateCoordinator
    {
        private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded), true);
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
                main.SuggestedChanges.Items.CollectionChanged += OnChanged;
                main.SuggestedChanges.PropertyChanged += OnPropertyChanged;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                QueueApplyGate();
            }

            private void OnChanged(object? sender, EventArgs e) => QueueApplyGate();
            private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueApplyGate();

            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or nameof(MainWindowViewModel.CurrentJourneyTarget))
                    QueueApplyGate();
            }

            private void QueueApplyGate()
            {
                if (_refreshPending || window.Dispatcher.HasShutdownStarted) return;
                _refreshPending = true;
                window.Dispatcher.BeginInvoke(
                    new Action(() => _ = ApplyGateSafeAsync()),
                    DispatcherPriority.ContextIdle);
            }

            private async Task ApplyGateSafeAsync()
            {
                try
                {
                    await ApplyGateAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Approval Queue journey gate refresh failed: {exception}");
                }
                finally
                {
                    _refreshPending = false;
                }
            }

            private async Task ApplyGateAsync()
            {
                await main.SuggestedChanges.RefreshApprovalJourneyReadinessAsync();
                main.RefreshFirstJourneySidebar();

                if (!main.Sites.IsFirstJourneyReady ||
                    !main.Explorer.IsFirstJourneyReady ||
                    !main.SeoAudit.IsFirstJourneyReady ||
                    !main.SuggestedChanges.IsFirstJourneyReady)
                    return;

                if (main.SuggestedChanges.IsApprovalJourneyReady)
                    return;

                main.ApplyApprovalQueueJourneyGate();
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.SuggestedChanges.Items.CollectionChanged -= OnChanged;
                main.SuggestedChanges.PropertyChanged -= OnPropertyChanged;
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
        internal void ApplyApprovalQueueJourneyGate()
        {
            CurrentJourneyStepTitle = "Approve execution queue";
            CurrentJourneyStepDescription = SuggestedChanges.ApprovalJourneyStatus;
            CurrentJourneyActionLabel = "Open Approval Queue";
            CurrentJourneyTarget = "Approval Queue";
        }
    }
}
