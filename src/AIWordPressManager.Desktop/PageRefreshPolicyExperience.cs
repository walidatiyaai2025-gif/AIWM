using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Keeps dashboard-only periodic work from leaking into Sites and other work pages.
/// This prevents transient redraws and auto-surface attempts while the user is working elsewhere.
/// </summary>
internal static class PageRefreshPolicyExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    [ModuleInitializer]
    internal static void Initialize() => EventManager.RegisterClassHandler(
        typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main) return;

        Attached.Add(window, new object());

        var timer = typeof(MainWindowViewModel)
            .GetField("_liveDashboardTimer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(main) as DispatcherTimer;

        if (timer is null) return;

        void ApplyPolicy()
        {
            var shouldRun = string.Equals(main.CurrentPage, "Dashboard", StringComparison.OrdinalIgnoreCase);
            if (shouldRun)
            {
                if (!timer.IsEnabled) timer.Start();
            }
            else
            {
                if (timer.IsEnabled) timer.Stop();
            }
        }

        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                ApplyPolicy();
        };

        window.Activated += (_, _) => ApplyPolicy();
        window.Deactivated += (_, _) =>
        {
            if (!string.Equals(main.CurrentPage, "Dashboard", StringComparison.OrdinalIgnoreCase))
                timer.Stop();
        };
        window.Closed += (_, _) => timer.Stop();

        ApplyPolicy();
    }
}
