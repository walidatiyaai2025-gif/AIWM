using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Connects screens that already have implemented ViewModels and LoadAsync methods
/// to the existing navigation and refresh workflow. This class deliberately adds no
/// new business feature; it only activates functionality that is already present.
/// </summary>
internal static class ExistingFunctionActivationExperience
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

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClicked),
            true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main) return;

        var state = new State(main);
        Attached.Add(window, state);

        main.PropertyChanged += state.OnMainPropertyChanged;
        window.PreviewKeyDown += state.OnPreviewKeyDown;
        window.Closed += (_, _) =>
        {
            main.PropertyChanged -= state.OnMainPropertyChanged;
            window.PreviewKeyDown -= state.OnPreviewKeyDown;
            state.Cancel();
        };

        _ = state.ActivateCurrentPageAsync();
    }

    private static void OnButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Window.GetWindow(button) is not MainWindow window) return;
        if (!Attached.TryGetValue(window, out var state)) return;
        if (!ReferenceEquals(button.Command, state.Main.RefreshCurrentPageCommand)) return;

        // Run after the existing command so this remains an additive wiring fix.
        window.Dispatcher.BeginInvoke(
            new Action(() => _ = state.ActivateCurrentPageAsync(force: true)),
            DispatcherPriority.ContextIdle);
    }

    private sealed class State(MainWindowViewModel main)
    {
        private CancellationTokenSource? _activationCts;
        private string? _lastActivatedPage;

        public MainWindowViewModel Main { get; } = main;

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                _ = ActivateCurrentPageAsync();
        }

        public void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.R || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return;
            if (sender is not MainWindow window) return;

            window.Dispatcher.BeginInvoke(
                new Action(() => _ = ActivateCurrentPageAsync(force: true)),
                DispatcherPriority.ContextIdle);
        }

        public async Task ActivateCurrentPageAsync(bool force = false)
        {
            var page = Main.CurrentPage;
            if (!force && string.Equals(page, _lastActivatedPage, StringComparison.Ordinal)) return;

            _activationCts?.Cancel();
            _activationCts?.Dispose();
            _activationCts = new CancellationTokenSource();
            var token = _activationCts.Token;

            try
            {
                switch (page)
                {
                    case "Content Planner":
                        await Main.ContentPlanner.LoadAsync();
                        break;
                    case "Article Generator":
                        await Main.ArticleGenerator.LoadAsync();
                        break;
                    case "Notification Center":
                        await Main.Jobs.LoadAsync();
                        Main.Jobs.MarkNotificationsRead();
                        break;
                    case "Activity Timeline":
                        await Main.Jobs.LoadAsync();
                        break;
                    default:
                        return;
                }

                token.ThrowIfCancellationRequested();
                _lastActivatedPage = page;
            }
            catch (OperationCanceledException)
            {
                // A newer navigation request superseded this activation.
            }
        }

        public void Cancel()
        {
            _activationCts?.Cancel();
            _activationCts?.Dispose();
            _activationCts = null;
        }
    }
}
