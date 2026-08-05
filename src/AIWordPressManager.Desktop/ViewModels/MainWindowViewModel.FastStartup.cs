namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Loads only the state required to render the first window. Heavy workspaces are
    /// hydrated by NavigateAsync when the user opens them.
    /// </summary>
    public async Task InitializeFastAsync(IProgress<StartupProgress>? startupProgress = null)
    {
        if (IsLoadingApplicationData)
            return;

        IsLoadingApplicationData = true;
        _isInitializing = true;
        DatabaseStatus = "Preparing SQLite data…";
        ApplicationDataStatus = "Loading the essential local workspace";

        // Startup progress remains on the splash screen only. Do not expose a second
        // operation overlay inside the main window after sign-in.
        IsOperationRunning = false;
        OperationProgress = 0;
        OperationTitle = "Ready";
        OperationDetail = "Background modules load when opened.";
        OperationStep = "Startup";

        try
        {
            startupProgress?.Report(StartupProgress.Create(
                54,
                "Loading sites",
                "Reading saved WordPress sites from SQLite"));
            await Sites.LoadAsync();

            startupProgress?.Report(StartupProgress.Create(
                68,
                "Loading settings",
                "Applying language, theme, AI, and reliability settings"));
            await Settings.LoadAsync();

            DashboardSelectedSite = Sites.SelectedSite?.Name ?? "No site selected";
            ConnectionStatus = Sites.SelectedSite is null
                ? "No site selected"
                : $"{Sites.SelectedSite.Name} • {Sites.SelectedSite.Status}";

            DatabaseStatus = "SQLite connected • essential data ready";
            ApplicationDataStatus = Sites.SelectedSite is null
                ? "Essential data loaded. Add a site to begin."
                : $"{Sites.SelectedSite.Name} is ready. Open a workspace to load its saved snapshot.";

            startupProgress?.Report(StartupProgress.Create(
                90,
                "Opening workspace",
                "The remaining modules will load only when opened"));
        }
        finally
        {
            _isInitializing = false;
            IsLoadingApplicationData = false;
            IsOperationRunning = false;
            OperationProgress = 0;
            OperationStep = "Ready";
            OperationTitle = "Ready";
            RefreshDashboard();
        }
    }
}
