using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class ExecutionCenterJourneyGateCoordinator
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
            private bool _isApplying;

            public void Attach()
            {
                main.ExecutionCenter.Items.CollectionChanged += OnChanged;
                main.ExecutionCenter.PropertyChanged += OnPropertyChanged;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                ApplyGate();
            }

            private void OnChanged(object? sender, EventArgs e) => ApplyGate();

            private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(ExecutionCenterViewModel.QueueState)
                    or nameof(ExecutionCenterViewModel.LastExecutionUtc)
                    or nameof(ExecutionCenterViewModel.LatestReceiptPath)
                    or nameof(ExecutionCenterViewModel.BeforeEvidencePath)
                    or nameof(ExecutionCenterViewModel.AfterEvidencePath)
                    or nameof(ExecutionCenterViewModel.EvidenceStatus))
                {
                    ApplyGate();
                }
            }

            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or nameof(MainWindowViewModel.CurrentJourneyTarget))
                    ApplyGate();
            }

            private void ApplyGate()
            {
                if (_isApplying) return;
                _isApplying = true;
                try
                {
                    main.ExecutionCenter.RefreshFirstJourneyReadiness();
                    main.RefreshFirstJourneySidebar();

                    if (!main.Sites.IsFirstJourneyReady || !main.Explorer.IsFirstJourneyReady || !main.SeoAudit.IsFirstJourneyReady ||
                        !main.SuggestedChanges.IsFirstJourneyReady || !main.SuggestedChanges.IsApprovalJourneyReady)
                        return;
                    if (main.ExecutionCenter.IsFirstJourneyReady) return;
                    main.ApplyExecutionCenterJourneyGate();
                }
                finally
                {
                    _isApplying = false;
                }
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                main.ExecutionCenter.Items.CollectionChanged -= OnChanged;
                main.ExecutionCenter.PropertyChanged -= OnPropertyChanged;
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
        internal void ApplyExecutionCenterJourneyGate()
        {
            CurrentJourneyStepTitle = "Execute approved changes";
            CurrentJourneyStepDescription = ExecutionCenter.FirstJourneyStatus;
            CurrentJourneyActionLabel = "Open Execution Center";
            CurrentJourneyTarget = "Execution Center";
        }
    }
}
