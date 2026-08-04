using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class JourneyCheckpointExperience
{
    private const string CheckpointFileName = "journey-checkpoint.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> SupportedPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard", "Sites", "WordPress Explorer", "Content Audit", "SEO Audit", "SEO History",
        "Broken Links", "Category Planner", "Content Planner", "Article Generator", "Internal Links",
        "Suggested Changes", "Approval Queue", "Execution Center", "Jobs", "Backups", "Reports",
        "Logs", "Settings", "Help", "AI Studio", "Action Center", "Deletion Center",
        "Theme Inspector", "Visual Inspector", "Visual WordPress Editor", "AI Site Brain",
        "AI Autopilot", "Evidence Center", "Scheduler Center", "AI Decision Center",
        "Transaction Center", "Operations Center", "Release Readiness", "Plugin Compatibility",
        "Health Center", "Performance"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (window.DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        window.Closed -= OnWindowClosed;
        window.Closed += OnWindowClosed;

        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => RestoreCheckpoint(window, viewModel)));
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is MainWindow { DataContext: MainWindowViewModel viewModel })
        {
            SaveCheckpoint(viewModel);
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private static void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
            return;

        if (e.PropertyName is nameof(MainWindowViewModel.CurrentPage)
            or nameof(MainWindowViewModel.CurrentJourneyStepTitle)
            or nameof(MainWindowViewModel.CurrentJourneyTarget)
            or nameof(MainWindowViewModel.DashboardJourneyProgress)
            or nameof(MainWindowViewModel.DashboardSelectedSite))
        {
            SaveCheckpoint(viewModel);
        }
    }

    private static void RestoreCheckpoint(MainWindow window, MainWindowViewModel viewModel)
    {
        try
        {
            var path = ResolveCheckpointPath();
            if (!File.Exists(path))
                return;

            var checkpoint = JsonSerializer.Deserialize<JourneyCheckpoint>(File.ReadAllText(path), JsonOptions);
            if (checkpoint is null || string.IsNullOrWhiteSpace(checkpoint.Page))
                return;

            if (!SupportedPages.Contains(checkpoint.Page))
                return;

            if (!checkpoint.Page.Equals("Dashboard", StringComparison.OrdinalIgnoreCase))
                viewModel.NavigateCommand.Execute(checkpoint.Page);

            window.Title = $"AI WordPress Management — Resume: {checkpoint.JourneyStep}";
        }
        catch
        {
            // A corrupt or unavailable checkpoint must never prevent startup.
        }
    }

    private static void SaveCheckpoint(MainWindowViewModel viewModel)
    {
        try
        {
            var path = ResolveCheckpointPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var checkpoint = new JourneyCheckpoint(
                viewModel.CurrentPage,
                viewModel.CurrentJourneyStepTitle,
                viewModel.CurrentJourneyTarget,
                viewModel.DashboardJourneyProgress,
                viewModel.DashboardSelectedSite,
                DateTimeOffset.UtcNow);

            File.WriteAllText(path, JsonSerializer.Serialize(checkpoint, JsonOptions));
        }
        catch
        {
            // Checkpoint persistence is non-critical and must not interrupt user work.
        }
    }

    private static string ResolveCheckpointPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager");
        return Path.Combine(root, CheckpointFileName);
    }

    private sealed record JourneyCheckpoint(
        string Page,
        string JourneyStep,
        string JourneyTarget,
        int Progress,
        string SiteDisplayName,
        DateTimeOffset SavedAtUtc);
}
