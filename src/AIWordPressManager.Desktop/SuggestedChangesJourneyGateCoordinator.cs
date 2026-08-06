using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop
{
    internal static class SuggestedChangesJourneyGateCoordinator
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
                main.SuggestedChanges.Items.CollectionChanged += OnChanged;
                main.SuggestedChanges.PropertyChanged += OnPropertyChanged;
                main.PropertyChanged += OnMainPropertyChanged;
                window.Closed += OnClosed;
                ApplyGate();
            }

            private void OnChanged(object? sender, EventArgs e) => ApplyGate();
            private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => ApplyGate();
            private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage) or nameof(MainWindowViewModel.CurrentJourneyTarget))
                    ApplyGate();
            }

            private void ApplyGate()
            {
                main.SuggestedChanges.RefreshFirstJourneyReadiness();
                main.RefreshFirstJourneySidebar();

                if (!main.Sites.IsFirstJourneyReady || !main.Explorer.IsFirstJourneyReady || !main.SeoAudit.IsFirstJourneyReady)
                    return;
                if (main.SuggestedChanges.IsFirstJourneyReady)
                    return;

                main.ApplySuggestedChangesJourneyGate();
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
        internal void ApplySuggestedChangesJourneyGate()
        {
            CurrentJourneyStepTitle = "Review suggested changes";
            CurrentJourneyStepDescription = SuggestedChanges.FirstJourneyStatus;
            CurrentJourneyActionLabel = "Open Suggested Changes";
            CurrentJourneyTarget = "Suggested Changes";
        }
    }
}
