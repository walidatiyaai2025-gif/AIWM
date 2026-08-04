using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime;
using AIWordPressManager.Desktop;
using AIWordPressManager.Desktop.Behaviors;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly ILocalizationService _localization;
    private readonly IApplicationPathService _applicationPaths;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastProcessorSampleUtc = DateTime.UtcNow;
    private bool _isInitializing;
    private bool _isReloadingSiteData;
    private readonly DispatcherTimer _liveDashboardTimer;
    private bool _liveDashboardTickBusy;
    private int _liveDashboardTick;
    private DateTime _lastMemoryCoolingActionUtc = DateTime.MinValue;
    private bool _memoryCoolingActionRunning;

    public ObservableCollection<DashboardChartItem> HealthChart { get; } = [];
    public ObservableCollection<DashboardActivityItem> RecentActivity { get; } = [];
    public ObservableCollection<DashboardPriorityAction> DashboardPriorities { get; } = [];

    [ObservableProperty] private int _dashboardHealthScore;
    [ObservableProperty] private string _dashboardSeoScoreState = "NOT ANALYZED";
    [ObservableProperty] private string _dashboardSeoScoreSummary = "Run the first website analysis to establish a measurable baseline.";
    [ObservableProperty] private string _dashboardEstimatedGain = "+0 points";
    [ObservableProperty] private int _dashboardProjectedScore;
    [ObservableProperty] private int _dashboardPerformanceScore;
    [ObservableProperty] private int _dashboardAccessibilityScore;
    [ObservableProperty] private int _dashboardContentQualityScore;
    [ObservableProperty] private int _dashboardTechnicalSeoScore;
    [ObservableProperty] private int _dashboardAiConfidence;
    [ObservableProperty] private string _dashboardLastScan = "Never scanned";
    [ObservableProperty] private string _dashboardExecutiveSummary = "Run the first analysis to build an executive website health baseline.";
    [ObservableProperty] private string _currentJourneyStepTitle = "Analyze website";
    [ObservableProperty] private string _currentJourneyStepDescription = "Run the first baseline analysis for SEO, content, links and taxonomy.";
    [ObservableProperty] private string _currentJourneyActionLabel = "Start analysis";
    [ObservableProperty] private string _currentJourneyTarget = "SEO Audit";
    [ObservableProperty] private int _dashboardJourneyProgress;
    [ObservableProperty] private bool _isGuidedAnalysisRunning;
    [ObservableProperty] private int _guidedAnalysisProgress;
    [ObservableProperty] private string _guidedAnalysisStage = "Ready to analyze";
    [ObservableProperty] private string _guidedAnalysisDetail = "Select a site, then start the guided optimization workflow.";
    [ObservableProperty] private bool _isSafeAutopilotRunning;
    [ObservableProperty] private int _safeAutopilotProgress;
    [ObservableProperty] private string _safeAutopilotStage = "Safe Autopilot is ready";
    [ObservableProperty] private string _safeAutopilotSummary = "Runs only approved low-risk actions through backup, WordPress write, verification and evidence capture.";
    [ObservableProperty] private string _safeAutopilotReadiness = "Select a site and complete the baseline analysis first.";
    [ObservableProperty] private string _lastOptimizationRunText = "No guided execution has been completed";
    [ObservableProperty] private string? _lastOptimizationReceiptPath;
    [ObservableProperty] private string _journeyAnalyzeState = "NOT STARTED";
    [ObservableProperty] private string _journeyAiReviewState = "NOT STARTED";
    [ObservableProperty] private string _journeyPreviewState = "NOT STARTED";
    [ObservableProperty] private string _journeyApprovalState = "NOT STARTED";
    [ObservableProperty] private string _journeyExecuteState = "NOT STARTED";
    [ObservableProperty] private string _journeyVerifyState = "NOT STARTED";
    [ObservableProperty] private string _journeyDoneState = "NOT STARTED";
    [ObservableProperty] private Brush _journeyAnalyzeBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyAiReviewBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyPreviewBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyApprovalBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyExecuteBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyVerifyBrush = Brushes.IndianRed;
    [ObservableProperty] private Brush _journeyDoneBrush = Brushes.IndianRed;
    [ObservableProperty] private int _dashboardOpenIssues;
    [ObservableProperty] private int _dashboardSafeActions;
    [ObservableProperty] private int _dashboardAiSuggestions;
    [ObservableProperty] private int _dashboardAutoFixCount;
    [ObservableProperty] private int _dashboardReviewCount;
    [ObservableProperty] private int _dashboardManualCount;
    [ObservableProperty] private string _dashboardHealthLabel = "Waiting for audit data";
    [ObservableProperty] private string _dashboardLiveClock = DateTime.Now.ToString("HH:mm:ss");
    [ObservableProperty] private string _dashboardLastRefresh = "Waiting for first refresh";
    [ObservableProperty] private string _dashboardLiveStatus = "LIVE • local data";
    [ObservableProperty] private int _dashboardRunningJobs;
    [ObservableProperty] private int _dashboardCompletedJobs;
    [ObservableProperty] private int _dashboardFailedJobs;
    [ObservableProperty] private int _dashboardExecutionProgress;
    [ObservableProperty] private string _dashboardExecutionStep = "Execution queue idle";
    [ObservableProperty] private bool _dashboardPulseOn;
    [ObservableProperty] private double _dashboardCpuPercent;
    [ObservableProperty] private long _dashboardMemoryMb;
    [ObservableProperty] private string _dashboardDatabaseSize = "0 KB";
    [ObservableProperty] private int _dashboardQueueTotal;
    [ObservableProperty] private string _dashboardWorkerState = "Idle";
    [ObservableProperty] private string _dashboardLastJob = "No jobs recorded";
    [ObservableProperty] private string _dashboardSelectedSite = "No site selected";
    [ObservableProperty] private string _dashboardLastSiteSync = "Never synchronized";
    [ObservableProperty] private double _systemMemoryUsagePercent;
    [ObservableProperty] private bool _isMemoryCooling;
    [ObservableProperty] private string _memoryCoolingStatus = "Memory usage is stable";
    [ObservableProperty] private string _memoryCleanupStatus = "Ready to release unused application memory";
    [ObservableProperty] private string _lastMemoryCleanupResult = "No manual cleanup has been run";
    [ObservableProperty] private bool _isMemoryCleanupRunning;

    public SitesViewModel Sites { get; }
    public WordPressExplorerViewModel Explorer { get; }
    public ContentAuditViewModel ContentAudit { get; }
    public SeoAuditViewModel SeoAudit { get; }
    public BrokenLinksViewModel BrokenLinks { get; }
    public CategoryPlannerViewModel CategoryPlanner { get; }
    public ContentPlannerViewModel ContentPlanner { get; }
    public ArticleGeneratorViewModel ArticleGenerator { get; }
    public InternalLinksViewModel InternalLinks { get; }
    public SuggestedChangesViewModel SuggestedChanges { get; }
    public SettingsViewModel Settings { get; }
    public AiStudioViewModel AiStudio { get; }
    public ActionCenterViewModel ActionCenter { get; }
    public DeletionCenterViewModel DeletionCenter { get; }
    public ThemeInspectorViewModel ThemeInspector { get; }
    public PostSeoEditorViewModel PostEditor { get; }
    public ExecutionCenterViewModel ExecutionCenter { get; }
    public JobsViewModel Jobs { get; }
    public VisualInspectorViewModel VisualInspector { get; }
    public VisualWordPressEditorViewModel VisualEditor { get; }
    public SiteBrainViewModel SiteBrain { get; }
    public AutopilotOrchestratorViewModel Orchestrator { get; }
    public EvidenceCenterViewModel EvidenceCenter { get; }
    public SchedulerCenterViewModel SchedulerCenter { get; }
    public AiDecisionCenterViewModel DecisionCenter { get; }
    public TransactionCenterViewModel TransactionCenter { get; }
    public OperationsCenterViewModel OperationsCenter { get; }
    public ReleaseReadinessViewModel ReleaseReadiness { get; }
    public PluginCompatibilityCenterViewModel PluginCompatibility { get; }
    public HealthCenterViewModel HealthCenter { get; }
    public BackupsViewModel Backups { get; }
    public ReportsViewModel Reports { get; }
    public LogsViewModel Logs { get; }
    public HelpViewModel Help { get; }
    public UiOperationService Operations { get; }
    public IAsyncRelayCommand<string?> NavigateCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand<string?> ApplyAccentPaletteCommand { get; }
    public IRelayCommand<string?> ApplyFontPaletteCommand { get; }
    public IRelayCommand ToggleLanguageCommand { get; }
    public IAsyncRelayCommand AddSiteCommand { get; }
    public IAsyncRelayCommand StartOptimizationCommand { get; }
    public IAsyncRelayCommand ContinueJourneyCommand { get; }
    public IAsyncRelayCommand RunSafeAutopilotCommand { get; }
    public IRelayCommand OpenLastOptimizationReceiptCommand { get; }
    public IAsyncRelayCommand RefreshSitesCommand { get; }
    public IAsyncRelayCommand ShowNotificationsCommand { get; }
    public IRelayCommand ToggleUserMenuCommand { get; }
    public IAsyncRelayCommand RefreshCurrentPageCommand { get; }
    public IAsyncRelayCommand CleanDeviceMemoryCommand { get; }

    [ObservableProperty] private string _pageTitle = "Dashboard";
    [ObservableProperty] private string _pageDescription = "Monitor website health, pending work, and recent activity.";
    [ObservableProperty] private string _databaseStatus = "SQLite connected";
    [ObservableProperty] private string _connectionStatus = "No site selected";
    [ObservableProperty] private string _currentPage = "Dashboard";
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private string _themeIcon = "☀";
    [ObservableProperty] private string _currentPaletteName = "Brand Teal";
    [ObservableProperty] private string _currentFontPaletteName = "Adaptive Contrast";
    [ObservableProperty] private string _languageLabel = "AR";
    [ObservableProperty] private FlowDirection _flowDirection = FlowDirection.LeftToRight;
    [ObservableProperty] private bool _isUserMenuOpen;
    [ObservableProperty] private bool _isLoadingApplicationData;
    [ObservableProperty] private string _applicationDataStatus = "Waiting to load local data";
    [ObservableProperty] private bool _isOperationRunning;
    [ObservableProperty] private int _operationProgress;
    [ObservableProperty] private string _operationTitle = "Ready";
    [ObservableProperty] private string _operationDetail = "No background operation is running.";
    [ObservableProperty] private string _operationStep = "Idle";


    public bool IsDashboardVisible => CurrentPage == "Dashboard";
    public bool IsSitesVisible => CurrentPage == "Sites";
    public bool IsExplorerVisible => CurrentPage == "WordPress Explorer";
    public bool IsJobsVisible => CurrentPage == "Jobs";
    public bool IsNotificationCenterVisible => CurrentPage == "Notification Center";
    public bool IsActivityTimelineVisible => CurrentPage == "Activity Timeline";
    public bool IsVisualInspectorVisible => CurrentPage == "Visual Inspector";
    public bool IsVisualEditorVisible => CurrentPage == "Visual WordPress Editor";
    public bool IsSiteBrainVisible => CurrentPage == "AI Site Brain";
    public bool IsOrchestratorVisible => CurrentPage == "AI Autopilot";
    public bool IsEvidenceCenterVisible => CurrentPage == "Evidence Center";
    public bool IsSchedulerCenterVisible => CurrentPage == "Scheduler Center";
    public bool IsDecisionCenterVisible => CurrentPage == "AI Decision Center";
    public bool IsTransactionCenterVisible => CurrentPage == "Transaction Center";
    public bool IsOperationsCenterVisible => CurrentPage == "Operations Center";
    public bool IsReleaseReadinessVisible => CurrentPage == "Release Readiness";
    public bool IsPluginCompatibilityVisible => CurrentPage == "Plugin Compatibility";
    public bool IsHealthCenterVisible => CurrentPage == "Health Center";
    public bool IsBackupsVisible => CurrentPage == "Backups";
    public bool IsReportsVisible => CurrentPage == "Reports";
    public bool IsLogsVisible => CurrentPage == "Logs";
    public bool IsHelpVisible => CurrentPage == "Help";
    public bool IsPerformanceVisible => CurrentPage == "Performance";
    public bool IsContentAuditVisible => CurrentPage == "Content Audit";
    public bool IsSeoAuditVisible => CurrentPage == "SEO Audit";
    public bool IsSeoHistoryVisible => CurrentPage == "SEO History";
    public bool IsBrokenLinksVisible => CurrentPage == "Broken Links";
    public bool IsCategoryPlannerVisible => CurrentPage == "Category Planner";
    public bool IsContentPlannerVisible => CurrentPage == "Content Planner";
    public bool IsArticleGeneratorVisible => CurrentPage == "Article Generator";
    public bool IsInternalLinksVisible => CurrentPage == "Internal Links";
    public bool IsSuggestedChangesVisible => CurrentPage == "Suggested Changes";
    public bool IsApprovalQueueVisible => CurrentPage == "Approval Queue";
    public bool IsSettingsVisible => CurrentPage == "Settings";
    public bool IsAiStudioVisible => CurrentPage == "AI Studio";
    public bool IsActionCenterVisible => CurrentPage == "Action Center";
    public bool IsDeletionCenterVisible => CurrentPage == "Deletion Center";
    public bool IsThemeInspectorVisible => CurrentPage == "Theme Inspector";
    public bool IsPostEditorVisible => CurrentPage == "Post SEO Editor";
    public bool IsExecutionCenterVisible => CurrentPage == "Execution Center";
    public bool IsPlaceholderVisible => !IsDashboardVisible && !IsSitesVisible && !IsExplorerVisible && !IsJobsVisible && !IsNotificationCenterVisible && !IsActivityTimelineVisible && !IsContentAuditVisible && !IsSeoAuditVisible && !IsSeoHistoryVisible && !IsBrokenLinksVisible && !IsCategoryPlannerVisible && !IsContentPlannerVisible && !IsArticleGeneratorVisible && !IsInternalLinksVisible && !IsSuggestedChangesVisible && !IsApprovalQueueVisible && !IsSettingsVisible && !IsAiStudioVisible && !IsActionCenterVisible && !IsDeletionCenterVisible && !IsThemeInspectorVisible && !IsPostEditorVisible && !IsExecutionCenterVisible && !IsVisualInspectorVisible && !IsVisualEditorVisible && !IsSiteBrainVisible && !IsOrchestratorVisible && !IsEvidenceCenterVisible && !IsSchedulerCenterVisible && !IsDecisionCenterVisible && !IsTransactionCenterVisible && !IsOperationsCenterVisible && !IsReleaseReadinessVisible && !IsPluginCompatibilityVisible && !IsHealthCenterVisible && !IsBackupsVisible && !IsReportsVisible && !IsLogsVisible && !IsHelpVisible && !IsPerformanceVisible;

    public MainWindowViewModel(
        SitesViewModel sites,
        WordPressExplorerViewModel explorer,
        ContentAuditViewModel contentAudit,
        SeoAuditViewModel seoAudit,
        BrokenLinksViewModel brokenLinks,
        CategoryPlannerViewModel categoryPlanner,
        ContentPlannerViewModel contentPlanner,
        ArticleGeneratorViewModel articleGenerator,
        InternalLinksViewModel internalLinks,
        SuggestedChangesViewModel suggestedChanges,
        SettingsViewModel settings,
        AiStudioViewModel aiStudio,
        ActionCenterViewModel actionCenter,
        DeletionCenterViewModel deletionCenter,
        ThemeInspectorViewModel themeInspector,
        PostSeoEditorViewModel postEditor,
        ExecutionCenterViewModel executionCenter,
        JobsViewModel jobs,
        VisualInspectorViewModel visualInspector,
        VisualWordPressEditorViewModel visualEditor,
        SiteBrainViewModel siteBrain,
        AutopilotOrchestratorViewModel orchestrator,
        EvidenceCenterViewModel evidenceCenter,
        SchedulerCenterViewModel schedulerCenter,
        AiDecisionCenterViewModel decisionCenter,
        TransactionCenterViewModel transactionCenter,
        OperationsCenterViewModel operationsCenter,
        ReleaseReadinessViewModel releaseReadiness,
        PluginCompatibilityCenterViewModel pluginCompatibility,
        HealthCenterViewModel healthCenter,
        BackupsViewModel backups,
        ReportsViewModel reports,
        LogsViewModel logs,
        HelpViewModel help,
        IThemeService themeService,
        IDialogService dialogService,
        UiOperationService operations,
        ILocalizationService localization,
        IApplicationPathService applicationPaths)
    {
        Sites = sites;
        Explorer = explorer;
        ContentAudit = contentAudit;
        SeoAudit = seoAudit;
        BrokenLinks = brokenLinks;
        CategoryPlanner = categoryPlanner;
        ContentPlanner = contentPlanner;
        ArticleGenerator = articleGenerator;
        InternalLinks = internalLinks;
        SuggestedChanges = suggestedChanges;
        Settings = settings;
        AiStudio = aiStudio;
        ActionCenter = actionCenter;
        DeletionCenter = deletionCenter;
        ThemeInspector = themeInspector;
        PostEditor = postEditor;
        ExecutionCenter = executionCenter;
        Jobs = jobs;
        VisualInspector = visualInspector;
        VisualEditor = visualEditor;
        SiteBrain = siteBrain;
        Orchestrator = orchestrator;
        EvidenceCenter = evidenceCenter;
        SchedulerCenter = schedulerCenter;
        DecisionCenter = decisionCenter;
        TransactionCenter = transactionCenter;
        OperationsCenter = operationsCenter;
        ReleaseReadiness = releaseReadiness;
        PluginCompatibility = pluginCompatibility;
        HealthCenter = healthCenter;
        HealthCenter.NavigationRequested += destination => _ = NavigateAsync(destination);
        PluginCompatibility.NavigationRequested += destination => _ = NavigateAsync(destination);
        DecisionCenter.NavigationRequested += destination => _ = NavigateAsync(destination);
        TransactionCenter.NavigationRequested += destination => _ = NavigateAsync(destination);
        OperationsCenter.NavigationRequested += destination => _ = NavigateAsync(destination);
        Orchestrator.NavigationRequested += destination => _ = NavigateAsync(destination);
        Backups = backups;
        Reports = reports;
        Logs = logs;
        Help = help;
        _themeService = themeService;
        CurrentPaletteName = _themeService.CurrentPalette;
        CurrentFontPaletteName = _themeService.CurrentFontPalette;
        IsDarkTheme = _themeService.IsDarkTheme;
        _dialogService = dialogService;
        Operations = operations;
        _localization = localization;
        _applicationPaths = applicationPaths;
        _lastProcessorTime = Process.GetCurrentProcess().TotalProcessorTime;
        _localization.ApplyEnglish();
        NavigateCommand = new AsyncRelayCommand<string?>(NavigateAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        ApplyAccentPaletteCommand = new RelayCommand<string?>(ApplyAccentPalette);
        ApplyFontPaletteCommand = new RelayCommand<string?>(ApplyFontPalette);
        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        AddSiteCommand = new AsyncRelayCommand(AddSiteAsync);
        StartOptimizationCommand = new AsyncRelayCommand(StartOptimizationAsync, () => !IsGuidedAnalysisRunning && !IsSafeAutopilotRunning && Sites.SelectedSite is not null);
        ContinueJourneyCommand = new AsyncRelayCommand(ContinueJourneyAsync, () => !IsGuidedAnalysisRunning && !IsSafeAutopilotRunning && Sites.SelectedSite is not null);
        RunSafeAutopilotCommand = new AsyncRelayCommand(RunSafeAutopilotAsync, () => !IsGuidedAnalysisRunning && !IsSafeAutopilotRunning && Sites.SelectedSite is not null);
        OpenLastOptimizationReceiptCommand = new RelayCommand(OpenLastOptimizationReceipt, () => !string.IsNullOrWhiteSpace(LastOptimizationReceiptPath) && File.Exists(LastOptimizationReceiptPath));
        RefreshSitesCommand = new AsyncRelayCommand(Sites.LoadAsync);
        ShowNotificationsCommand = new AsyncRelayCommand(ShowNotificationsAsync);
        ToggleUserMenuCommand = new RelayCommand(() => IsUserMenuOpen = !IsUserMenuOpen);
        RefreshCurrentPageCommand = new AsyncRelayCommand(RefreshCurrentPageAsync);
        CleanDeviceMemoryCommand = new AsyncRelayCommand(CleanDeviceMemoryAsync, () => !IsMemoryCleanupRunning);
        ActionCenter.NavigationRequested += destination => _ = NavigateAsync(destination);
        Sites.SelectedSiteChanged += (_, _) =>
        {
            ConnectionStatus = Sites.SelectedSite is null
                ? "No site selected"
                : $"{Sites.SelectedSite.Name} • {Sites.SelectedSite.Status}";
            DashboardSelectedSite = Sites.SelectedSite?.Name ?? "No site selected";
            StartOptimizationCommand.NotifyCanExecuteChanged();
            ContinueJourneyCommand.NotifyCanExecuteChanged();
            RunSafeAutopilotCommand.NotifyCanExecuteChanged();
            UpdateSafeAutopilotReadiness();
            UpdateDashboardLastSiteSync();
            RefreshDashboard();
            if (!_isInitializing)
                _ = ReloadSelectedSiteDataAsync();
        };
        _liveDashboardTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _liveDashboardTimer.Tick += async (_, _) => await UpdateLiveDashboardAsync();
        _liveDashboardTimer.Start();
        RefreshDashboard();
    }

    public async Task InitializeAsync(IProgress<StartupProgress>? startupProgress = null)
    {
        if (IsLoadingApplicationData) return;
        IsLoadingApplicationData = true;
        startupProgress?.Report(StartupProgress.Create(40, "Loading local data", "Preparing SQLite and saved site data"));
        IsOperationRunning = true;
        OperationProgress = 3;
        OperationTitle = "Loading application data";
        OperationDetail = "Preparing SQLite and saved site data";
        OperationStep = "Startup";
        _isInitializing = true;
        DatabaseStatus = "Loading SQLite data…";
        ApplicationDataStatus = "Loading sites and saved screen data from SQLite";
        try
        {
            OperationProgress = 10;
            OperationDetail = "Loading saved sites";
            startupProgress?.Report(StartupProgress.Create(48, "Loading sites", "Reading saved WordPress sites from SQLite"));
            await Sites.LoadAsync();
            OperationProgress = 18;
            OperationDetail = "Loading application and AI settings";
            startupProgress?.Report(StartupProgress.Create(56, "Loading settings", "Applying language, performance, AI, and job reliability settings"));
            await Settings.LoadAsync();
            OperationProgress = 24;
            startupProgress?.Report(StartupProgress.Create(63, "Loading offline workspace", "Preparing the selected site and saved screen data"));
            await ReloadSelectedSiteDataAsync(startupProgress);
            OperationProgress = 100;
            OperationDetail = "All saved screens are ready";
            startupProgress?.Report(StartupProgress.Create(92, "Finalizing workspace", "Refreshing dashboard indicators and restoring the last selected site"));
            DatabaseStatus = "SQLite connected • offline data loaded";
            ApplicationDataStatus = Sites.SelectedSite is null
                ? "Application data loaded. Add a site to begin."
                : $"All saved screens loaded for {Sites.SelectedSite.Name}.";
        }
        finally
        {
            _isInitializing = false;
            IsLoadingApplicationData = false;
            IsOperationRunning = false;
            OperationStep = "Ready";
            OperationTitle = "Local data ready";
            RefreshDashboard();
        }
    }

    private async Task ReloadSelectedSiteDataAsync(IProgress<StartupProgress>? startupProgress = null)
    {
        if (_isReloadingSiteData || Sites.SelectedSite is null) return;
        _isReloadingSiteData = true;
        try
        {
            ApplicationDataStatus = $"Loading saved data for {Sites.SelectedSite.Name}…";
            IsOperationRunning = true;
            OperationTitle = $"Loading {Sites.SelectedSite.Name}";
            OperationStep = "Offline startup cache";

            // Startup must remain deterministic. Only data required by the dashboard is loaded
            // before the main window is shown. The remaining screens are hydrated afterwards.
            var essentialLoaders = new (string Name, Func<Task> Load)[]
            {
                ("WordPress Explorer", Explorer.LoadAsync),
                ("Content Audit", ContentAudit.LoadAsync),
                ("SEO Audit", SeoAudit.LoadAsync),
                ("Broken Links", BrokenLinks.LoadAsync),
                ("Suggested Changes", SuggestedChanges.ShowAllAsync),
                ("Jobs", Jobs.LoadAsync)
            };

            for (var index = 0; index < essentialLoaders.Length; index++)
            {
                OperationDetail = $"Loading {essentialLoaders[index].Name} from SQLite";
                OperationProgress = 24 + (int)Math.Round((index + 1) * 76d / essentialLoaders.Length);
                var splashPercent = 63 + (int)Math.Round((index + 1) * 27d / essentialLoaders.Length);
                startupProgress?.Report(StartupProgress.Create(
                    splashPercent,
                    "Loading essential workspace",
                    $"{essentialLoaders[index].Name} • {index + 1} of {essentialLoaders.Length}"));

                await LoadSafelyAsync(
                    essentialLoaders[index].Name,
                    essentialLoaders[index].Load,
                    TimeSpan.FromSeconds(4));
            }

            ApplicationDataStatus = $"Essential SQLite data is ready for {Sites.SelectedSite.Name}. Remaining screens will load in the background.";
            OperationDetail = "Dashboard data loaded; deferred screen loading will continue after startup";
        }
        finally
        {
            _isReloadingSiteData = false;
            if (!_isInitializing)
            {
                IsOperationRunning = false;
                OperationTitle = "Local data refreshed";
                OperationStep = "Ready";
            }
            RefreshDashboard();
        }
    }

    public async Task LoadDeferredSiteDataAsync()
    {
        if (Sites.SelectedSite is null) return;

        var deferredLoaders = new (string Name, Func<Task> Load)[]
        {
            ("Category Planner", CategoryPlanner.LoadAsync),
            ("Content Planner", ContentPlanner.LoadAsync),
            ("Internal Links", InternalLinks.LoadAsync),
            ("AI Studio", AiStudio.LoadAsync),
            ("Action Center", ActionCenter.LoadAsync),
            ("Theme Inspector", ThemeInspector.LoadOfflineAsync),
            ("Deletion Center", DeletionCenter.LoadAsync),
            ("Post SEO Editor", PostEditor.LoadOfflineAsync),
            ("Execution Center", ExecutionCenter.LoadAsync),
            ("Visual Inspector", VisualInspector.LoadAsync),
            ("Visual WordPress Editor", VisualEditor.LoadAsync),
            ("AI Site Brain", SiteBrain.LoadAsync),
            ("AI Autopilot", Orchestrator.LoadAsync),
            ("Evidence Center", EvidenceCenter.LoadAsync),
            ("Scheduler Center", SchedulerCenter.LoadAsync),
            ("AI Decision Center", DecisionCenter.LoadAsync),
            ("Transaction Center", TransactionCenter.LoadAsync),
            ("Operations Center", OperationsCenter.LoadAsync),
            ("Plugin Compatibility", PluginCompatibility.LoadAsync),
            ("Health Center", HealthCenter.LoadAsync),
            ("Backups", Backups.LoadAsync),
            ("Reports", Reports.LoadAsync),
            ("Logs", Logs.LoadAsync)
        };

        foreach (var loader in deferredLoaders)
        {
            await LoadSafelyAsync(loader.Name, loader.Load, TimeSpan.FromSeconds(8));
            await Task.Yield();
        }

        ApplicationDataStatus = $"All saved screens loaded for {Sites.SelectedSite.Name}.";
        RefreshDashboard();
    }

    private async Task LoadSafelyAsync(string module, Func<Task> loader, TimeSpan? timeout = null)
    {
        try
        {
            if (!_isInitializing && !_isReloadingSiteData)
            {
                IsOperationRunning = true;
                OperationProgress = 35;
                OperationTitle = $"Loading {module}";
                OperationDetail = "Reading the latest saved data from SQLite";
                OperationStep = module;
            }
            var loadTask = loader();
            if (timeout is { } maxDuration)
            {
                await loadTask.WaitAsync(maxDuration);
            }
            else
            {
                await loadTask;
            }
            if (!_isInitializing && !_isReloadingSiteData)
            {
                OperationProgress = 100;
                OperationDetail = $"{module} is ready";
            }
        }
        catch (TimeoutException)
        {
            ApplicationDataStatus = $"{module} exceeded the startup time limit and will remain available for manual refresh.";
            OperationDetail = ApplicationDataStatus;
        }
        catch (Exception exception)
        {
            ApplicationDataStatus = $"{module} could not load: {exception.Message}";
            OperationDetail = ApplicationDataStatus;
        }
        finally
        {
            if (!_isInitializing && !_isReloadingSiteData)
            {
                IsOperationRunning = false;
                OperationStep = "Ready";
            }
        }
    }

    private async Task NavigateAsync(string? destination)
    {
        CurrentPage = string.IsNullOrWhiteSpace(destination) ? "Dashboard" : destination;
        UpdatePageMetadata(CurrentPage);
        IsUserMenuOpen = false;
        RaisePageVisibility();
        if (CurrentPage == "Dashboard") RefreshDashboard();
        if (CurrentPage == "Sites") await LoadSafelyAsync("Sites", Sites.LoadAsync);
        if (CurrentPage == "WordPress Explorer") await LoadSafelyAsync("WordPress Explorer", Explorer.LoadAsync);
        if (CurrentPage == "Content Audit") await LoadSafelyAsync("Content Audit", ContentAudit.LoadAsync);
        if (CurrentPage == "SEO Audit") await LoadSafelyAsync("SEO Audit", SeoAudit.LoadAsync);
        if (CurrentPage == "SEO History") await LoadSafelyAsync("SEO History", SeoAudit.LoadAsync);
        if (CurrentPage == "Broken Links") await LoadSafelyAsync("Broken Links", BrokenLinks.LoadAsync);
        if (CurrentPage == "Category Planner") await LoadSafelyAsync("Category Planner", CategoryPlanner.LoadAsync);
        if (CurrentPage == "Content Planner") await LoadSafelyAsync("Content Planner", ContentPlanner.LoadAsync);
        if (CurrentPage == "Article Generator") await LoadSafelyAsync("Article Generator", ArticleGenerator.LoadAsync);
        if (CurrentPage == "Internal Links") await LoadSafelyAsync("Internal Links", InternalLinks.LoadAsync);
        if (CurrentPage == "Suggested Changes") await LoadSafelyAsync("Suggested Changes", SuggestedChanges.ShowAllAsync);
        if (CurrentPage == "Approval Queue") await LoadSafelyAsync("Approval Queue", SuggestedChanges.ShowApprovalQueueAsync);
        if (CurrentPage == "Settings") await LoadSafelyAsync("Settings", Settings.LoadAsync);
        if (CurrentPage == "AI Studio") await LoadSafelyAsync("AI Studio", AiStudio.LoadAsync);
        if (CurrentPage == "Action Center") await LoadSafelyAsync("Action Center", ActionCenter.LoadAsync);
        if (CurrentPage == "Deletion Center") await LoadSafelyAsync("Deletion Center", DeletionCenter.LoadAsync);
        if (CurrentPage == "Theme Inspector") await LoadSafelyAsync("Theme Inspector", ThemeInspector.LoadOfflineAsync);
        if (CurrentPage == "Post SEO Editor") await LoadSafelyAsync("Post SEO Editor", PostEditor.LoadOfflineAsync);
        if (CurrentPage == "Execution Center") await LoadSafelyAsync("Execution Center", ExecutionCenter.LoadAsync);
        if (CurrentPage == "Jobs") await LoadSafelyAsync("Jobs", Jobs.LoadAsync);
        if (CurrentPage == "Visual Inspector") await LoadSafelyAsync("Visual Inspector", VisualInspector.LoadAsync);
        if (CurrentPage == "Visual WordPress Editor") await LoadSafelyAsync("Visual WordPress Editor", VisualEditor.LoadAsync);
        if (CurrentPage == "AI Site Brain") await LoadSafelyAsync("AI Site Brain", SiteBrain.LoadAsync);
        if (CurrentPage == "AI Autopilot") await LoadSafelyAsync("AI Autopilot", Orchestrator.LoadAsync);
        if (CurrentPage == "Evidence Center") await LoadSafelyAsync("Evidence Center", EvidenceCenter.LoadAsync);
        if (CurrentPage == "Scheduler Center") await LoadSafelyAsync("Scheduler Center", SchedulerCenter.LoadAsync);
        if (CurrentPage == "AI Decision Center") await LoadSafelyAsync("AI Decision Center", DecisionCenter.LoadAsync);
        if (CurrentPage == "Transaction Center") await LoadSafelyAsync("Transaction Center", TransactionCenter.LoadAsync);
        if (CurrentPage == "Operations Center") await LoadSafelyAsync("Operations Center", OperationsCenter.LoadAsync);
        if (CurrentPage == "Release Readiness") await LoadSafelyAsync("Release Readiness", ReleaseReadiness.LoadAsync);
        if (CurrentPage == "Plugin Compatibility") await LoadSafelyAsync("Plugin Compatibility", PluginCompatibility.LoadAsync);
        if (CurrentPage == "Health Center") await LoadSafelyAsync("Health Center", HealthCenter.LoadAsync);
        if (CurrentPage == "Backups") await LoadSafelyAsync("Backups", Backups.LoadAsync);
        if (CurrentPage == "Reports") await LoadSafelyAsync("Reports", Reports.LoadAsync);
        if (CurrentPage == "Logs") await LoadSafelyAsync("Logs", Logs.LoadAsync);
    }


    partial void OnIsGuidedAnalysisRunningChanged(bool value)
    {
        StartOptimizationCommand.NotifyCanExecuteChanged();
        ContinueJourneyCommand.NotifyCanExecuteChanged();
        RunSafeAutopilotCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSafeAutopilotRunningChanged(bool value)
    {
        StartOptimizationCommand.NotifyCanExecuteChanged();
        ContinueJourneyCommand.NotifyCanExecuteChanged();
        RunSafeAutopilotCommand.NotifyCanExecuteChanged();
    }

    partial void OnLastOptimizationReceiptPathChanged(string? value)
        => OpenLastOptimizationReceiptCommand.NotifyCanExecuteChanged();

    private void UpdateSafeAutopilotReadiness()
    {
        if (Sites.SelectedSite is null)
        {
            SafeAutopilotReadiness = "Select a website first.";
            return;
        }

        if (DashboardSeoScoreState.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase))
        {
            SafeAutopilotReadiness = "Baseline analysis is required before safe execution.";
            return;
        }

        var blockers = new List<string>();
        if (!Settings.CaptureBeforeAfterEvidence) blockers.Add("before/after evidence is disabled");
        if (!Settings.RequireVerifiedExecutionResult) blockers.Add("post-write verification is disabled");
        if (!Settings.AutoRejectHighRiskAiActions) blockers.Add("high-risk automatic rejection is disabled");

        SafeAutopilotReadiness = blockers.Count == 0
            ? $"READY • {DashboardAutoFixCount:N0} low-risk supported actions can enter the verified pipeline."
            : $"BLOCKED • {string.Join("; ", blockers)}.";
    }

    private async Task RunSafeAutopilotAsync()
    {
        if (Sites.SelectedSite is null)
        {
            await _dialogService.ShowInformationAsync("Safe Autopilot", "Select the website you want to optimize first.");
            return;
        }

        if (DashboardSeoScoreState.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase))
        {
            await StartOptimizationAsync();
            RefreshDashboard();
        }

        UpdateSafeAutopilotReadiness();
        if (!Settings.CaptureBeforeAfterEvidence || !Settings.RequireVerifiedExecutionResult || !Settings.AutoRejectHighRiskAiActions)
        {
            await _dialogService.ShowInformationAsync(
                "Safe Autopilot is blocked",
                "Enable before/after evidence, verified execution results, and automatic rejection of high-risk actions in Settings > AI Automation.");
            await NavigateAsync("Settings");
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Run Safe Autopilot",
            $"Site: {Sites.SelectedSite.Name}\n\nThe application will approve low-risk supported actions only, create backups, write through the existing WordPress execution service, verify every saved value, and retain evidence. High-risk, staging, unsupported, and incomplete actions remain blocked.\n\nContinue?");
        if (!confirmed) return;

        IsSafeAutopilotRunning = true;
        SafeAutopilotProgress = 3;
        SafeAutopilotStage = "Creating guided execution run";
        SafeAutopilotSummary = "Preparing the current site's AI actions and safety policy.";
        var started = DateTimeOffset.Now;
        var scoreBefore = DashboardHealthScore;
        var readyBefore = DashboardAutoFixCount;
        var failure = string.Empty;

        try
        {
            SafeAutopilotProgress = 12;
            SafeAutopilotStage = "Refreshing AI actions";
            if (SuggestedChanges.Items.Count == 0 && SuggestedChanges.GenerateCommand.CanExecute(null))
                await SuggestedChanges.GenerateCommand.ExecuteAsync(null);

            SafeAutopilotProgress = 25;
            SafeAutopilotStage = "Loading execution queue";
            if (ExecutionCenter.LoadCommand.CanExecute(null))
                await ExecutionCenter.LoadCommand.ExecuteAsync(null);

            SafeAutopilotProgress = 38;
            SafeAutopilotStage = "Approving low-risk actions";
            if (ExecutionCenter.ApproveAllLowRiskCommand.CanExecute(null))
                await ExecutionCenter.ApproveAllLowRiskCommand.ExecuteAsync(null);

            SafeAutopilotProgress = 52;
            SafeAutopilotStage = "Preparing supported adapters";
            if (ExecutionCenter.PrepareAllSupportedCommand.CanExecute(null))
                await ExecutionCenter.PrepareAllSupportedCommand.ExecuteAsync(null);

            SafeAutopilotProgress = 66;
            SafeAutopilotStage = "Building verified execution plan";
            ExecutionCenter.BuildPlanCommand.Execute(null);

            SafeAutopilotProgress = 74;
            SafeAutopilotStage = "Executing safe WordPress plan";
            SafeAutopilotSummary = "Backup → WordPress write → read-back verification → evidence → transaction history.";
            if (ExecutionCenter.RunSafePlanCommand.CanExecute(null))
                await ExecutionCenter.RunSafePlanCommand.ExecuteAsync(null);

            SafeAutopilotProgress = 92;
            SafeAutopilotStage = "Refreshing verification evidence";
            if (EvidenceCenter.LoadCommand.CanExecute(null))
                await EvidenceCenter.LoadCommand.ExecuteAsync(null);
            if (TransactionCenter.LoadCommand.CanExecute(null))
                await TransactionCenter.LoadCommand.ExecuteAsync(null);

            RefreshDashboard();
            SafeAutopilotProgress = 100;
            SafeAutopilotStage = "Safe Autopilot completed";
            SafeAutopilotSummary = $"Executed {ExecutionCenter.ExecutedCount:N0}; failed {ExecutionCenter.FailedCount:N0}; current projected score {DashboardProjectedScore}/100.";
            LastOptimizationRunText = $"Completed {DateTime.Now:g} • {Sites.SelectedSite.Name}";
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            SafeAutopilotStage = "Safe Autopilot stopped";
            SafeAutopilotSummary = ex.Message;
            await _dialogService.ShowErrorAsync("Safe Autopilot stopped", ex.Message);
        }
        finally
        {
            var finished = DateTimeOffset.Now;
            LastOptimizationReceiptPath = await WriteOptimizationReceiptAsync(
                Sites.SelectedSite.Name,
                started,
                finished,
                scoreBefore,
                DashboardHealthScore,
                readyBefore,
                ExecutionCenter.ExecutedCount,
                ExecutionCenter.FailedCount,
                failure);
            IsSafeAutopilotRunning = false;
            UpdateSafeAutopilotReadiness();
        }
    }

    private async Task<string> WriteOptimizationReceiptAsync(
        string siteName,
        DateTimeOffset started,
        DateTimeOffset finished,
        int scoreBefore,
        int scoreAfter,
        int readyBefore,
        int executed,
        int failed,
        string failure)
    {
        var root = Path.Combine(_applicationPaths.GetApplicationDataDirectory(), "OptimizationRuns");
        Directory.CreateDirectory(root);
        var safeSite = string.Concat(siteName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(root, $"optimization-{safeSite}-{finished:yyyyMMdd-HHmmss}.md");
        var status = string.IsNullOrWhiteSpace(failure) ? "Completed" : "Stopped";
        var lines = new[]
        {
            $"# AI WordPress Optimization Run",
            string.Empty,
            $"- Site: {siteName}",
            $"- Status: {status}",
            $"- Started: {started:O}",
            $"- Finished: {finished:O}",
            $"- Duration: {(finished - started):g}",
            $"- SEO score before: {scoreBefore}/100",
            $"- SEO score after local refresh: {scoreAfter}/100",
            $"- Safe actions available before run: {readyBefore}",
            $"- Executed queue total: {executed}",
            $"- Failed queue total: {failed}",
            $"- Evidence required: {Settings.CaptureBeforeAfterEvidence}",
            $"- Verification required: {Settings.RequireVerifiedExecutionResult}",
            $"- High-risk auto rejection: {Settings.AutoRejectHighRiskAiActions}",
            string.Empty,
            "## Execution contract",
            string.Empty,
            "Low-risk supported actions only. Backup, WordPress response logging, read-back verification, evidence capture, transaction journal, and rollback availability remain enforced by the existing execution pipeline.",
            string.Empty,
            "## Error",
            string.Empty,
            string.IsNullOrWhiteSpace(failure) ? "None" : failure
        };
        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    private void OpenLastOptimizationReceipt()
    {
        if (string.IsNullOrWhiteSpace(LastOptimizationReceiptPath) || !File.Exists(LastOptimizationReceiptPath)) return;
        Process.Start(new ProcessStartInfo(LastOptimizationReceiptPath) { UseShellExecute = true });
    }

    private async Task ContinueJourneyAsync()
    {
        if (Sites.SelectedSite is null)
        {
            await _dialogService.ShowInformationAsync("Select a website", "Choose the website you want to optimize before continuing.");
            return;
        }

        if (DashboardSeoScoreState.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase))
        {
            await StartOptimizationAsync();
            return;
        }

        await NavigateAsync(CurrentJourneyTarget);
    }

    private async Task StartOptimizationAsync()
    {
        if (Sites.SelectedSite is null)
        {
            await _dialogService.ShowInformationAsync("Start optimization", "Select a website first.");
            return;
        }

        if (IsGuidedAnalysisRunning) return;

        IsGuidedAnalysisRunning = true;
        GuidedAnalysisProgress = 2;
        GuidedAnalysisStage = "Preparing website analysis";
        GuidedAnalysisDetail = $"Loading the saved workspace for {Sites.SelectedSite.Name}.";

        try
        {
            GuidedAnalysisProgress = 10;
            GuidedAnalysisStage = "1 of 5 • SEO audit";
            GuidedAnalysisDetail = "Checking titles, metadata, headings, indexability, and search requirements.";
            if (SeoAudit.RunAuditCommand.CanExecute(null))
                await SeoAudit.RunAuditCommand.ExecuteAsync(null);

            GuidedAnalysisProgress = 32;
            GuidedAnalysisStage = "2 of 5 • Content audit";
            GuidedAnalysisDetail = "Checking content depth, structure, readability, and missing optimization opportunities.";
            if (ContentAudit.RunAuditCommand.CanExecute(null))
                await ContentAudit.RunAuditCommand.ExecuteAsync(null);

            GuidedAnalysisProgress = 52;
            GuidedAnalysisStage = "3 of 5 • Link health";
            GuidedAnalysisDetail = "Checking broken, redirected, and healthy links.";
            if (BrokenLinks.RunScanCommand.CanExecute(null))
                await BrokenLinks.RunScanCommand.ExecuteAsync(null);

            GuidedAnalysisProgress = 68;
            GuidedAnalysisStage = "4 of 5 • Taxonomy health";
            GuidedAnalysisDetail = "Checking categories and topical organization.";
            if (CategoryPlanner.AnalyzeCommand.CanExecute(null))
                await CategoryPlanner.AnalyzeCommand.ExecuteAsync(null);

            GuidedAnalysisProgress = 84;
            GuidedAnalysisStage = "5 of 5 • AI action plan";
            GuidedAnalysisDetail = "Converting audit findings into reviewable and executable actions.";
            if (SuggestedChanges.GenerateCommand.CanExecute(null))
                await SuggestedChanges.GenerateCommand.ExecuteAsync(null);

            RefreshDashboard();
            GuidedAnalysisProgress = 100;
            GuidedAnalysisStage = "Analysis completed";
            GuidedAnalysisDetail = $"SEO baseline {DashboardHealthScore}/100 saved locally. Continue to AI Review.";

            await _dialogService.ShowInformationAsync(
                "Website analysis completed",
                $"SEO score: {DashboardHealthScore}/100\nOpen findings: {DashboardOpenIssues:N0}\nAI actions: {DashboardAiSuggestions:N0}\n\nThe next step is AI Review.");

            await NavigateAsync("Suggested Changes");
        }
        catch (Exception ex)
        {
            GuidedAnalysisStage = "Analysis stopped";
            GuidedAnalysisDetail = ex.Message;
            await _dialogService.ShowInformationAsync("Website analysis stopped", ex.Message);
        }
        finally
        {
            IsGuidedAnalysisRunning = false;
        }
    }

    private void RefreshDashboard()
    {
        UpdateDashboardLastSiteSync();
        var seoScore = Math.Clamp(SeoAudit.Score, 0, 100);
        var contentScore = Math.Clamp(ContentAudit.Score, 0, 100);
        var linkTotal = BrokenLinks.HealthyCount + BrokenLinks.BrokenCount + BrokenLinks.RedirectCount;
        var linkScore = linkTotal == 0 ? 0 : (int)Math.Round(BrokenLinks.HealthyCount * 100d / linkTotal);
        var categoryTotal = CategoryPlanner.HealthyCategories + CategoryPlanner.WeakCategories + CategoryPlanner.EmptyCategories;
        var categoryScore = categoryTotal == 0 ? 0 : (int)Math.Round(CategoryPlanner.HealthyCategories * 100d / categoryTotal);

        var hasBaseline = SeoAudit.AuditedItems > 0 || ContentAudit.AuditedItems > 0 || linkTotal > 0 || categoryTotal > 0;
        DashboardHealthScore = hasBaseline
            ? (int)Math.Round((seoScore * 0.45d) + (contentScore * 0.30d) + (linkScore * 0.15d) + (categoryScore * 0.10d))
            : 0;
        DashboardHealthLabel = DashboardHealthScore switch
        {
            >= 85 => "Strong SEO foundation",
            >= 70 => "Good foundation with clear opportunities",
            >= 50 => "Optimization required",
            > 0 => "Priority SEO work required",
            _ => "Website baseline has not been analyzed"
        };
        DashboardSeoScoreState = hasBaseline ? "BASELINE READY" : "NOT ANALYZED";
        DashboardSeoScoreSummary = hasBaseline
            ? $"Weighted baseline from SEO, content, links and taxonomy. Last refresh: {DateTime.Now:HH:mm}."
            : "Select a site and start the first analysis. The system will save the score as the before snapshot.";

        DashboardOpenIssues = SeoAudit.Issues.Count + ContentAudit.Issues.Count + BrokenLinks.BrokenCount;
        DashboardAiSuggestions = SuggestedChanges.Items.Count;
        DashboardSafeActions = SuggestedChanges.Items.Count(x =>
            x.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase) &&
            !x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));
        DashboardAutoFixCount = SuggestedChanges.Items.Count(x => x.CanApplyDirectly && x.RiskLevel.Equals("Low", StringComparison.OrdinalIgnoreCase) && !x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));
        DashboardReviewCount = SuggestedChanges.Items.Count(x => x.RiskLevel.Equals("Medium", StringComparison.OrdinalIgnoreCase) && !x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));
        DashboardManualCount = SuggestedChanges.Items.Count(x => x.RiskLevel.Equals("High", StringComparison.OrdinalIgnoreCase) || !x.CanApplyDirectly);

        var analyzed = hasBaseline;
        var reviewed = SuggestedChanges.Items.Count > 0;
        var previewed = SuggestedChanges.Items.Any(x => !string.IsNullOrWhiteSpace(x.ProposedValue));
        var approved = SuggestedChanges.Items.Any(x => x.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase));
        var executed = SuggestedChanges.Items.Any(x => x.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase));
        var verified = executed && Jobs.Items.Any(x => x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase));
        var completed = verified && DashboardFailedJobs == 0;

        SetJourneyState(analyzed, false, out var analyzeState, out var analyzeBrush);
        SetJourneyState(reviewed, analyzed && !reviewed, out var reviewState, out var reviewBrush);
        SetJourneyState(previewed, reviewed && !previewed, out var previewState, out var previewBrush);
        SetJourneyState(approved, previewed && !approved, out var approvalState, out var approvalBrush);
        SetJourneyState(executed, approved && !executed, out var executeState, out var executeBrush);
        SetJourneyState(verified, executed && !verified, out var verifyState, out var verifyBrush);
        SetJourneyState(completed, verified && !completed, out var doneState, out var doneBrush);

        JourneyAnalyzeState = analyzeState; JourneyAnalyzeBrush = analyzeBrush;
        JourneyAiReviewState = reviewState; JourneyAiReviewBrush = reviewBrush;
        JourneyPreviewState = previewState; JourneyPreviewBrush = previewBrush;
        JourneyApprovalState = approvalState; JourneyApprovalBrush = approvalBrush;
        JourneyExecuteState = executeState; JourneyExecuteBrush = executeBrush;
        JourneyVerifyState = verifyState; JourneyVerifyBrush = verifyBrush;
        JourneyDoneState = doneState; JourneyDoneBrush = doneBrush;
        DashboardJourneyProgress = new[] { analyzed, reviewed, previewed, approved, executed, verified, completed }.Count(x => x) * 100 / 7;
        var potentialPoints = Math.Min(30, (int)Math.Ceiling(DashboardOpenIssues * 0.08d) + DashboardAutoFixCount / 5);
        DashboardProjectedScore = Math.Min(100, DashboardHealthScore + potentialPoints);
        DashboardEstimatedGain = $"+{potentialPoints} points potential";

        DashboardContentQualityScore = contentScore;
        DashboardTechnicalSeoScore = seoScore;
        DashboardPerformanceScore = Math.Clamp(100 - Math.Min(70, BrokenLinks.BrokenCount * 3 + DashboardFailedJobs * 5), 0, 100);
        DashboardAccessibilityScore = Math.Clamp(92 - Math.Min(55, DashboardManualCount / 3), 0, 100);
        DashboardAiConfidence = reviewed
            ? Math.Clamp(65 + Math.Min(30, DashboardAutoFixCount / 2) - Math.Min(20, DashboardManualCount / 10), 0, 99)
            : 0;
        DashboardLastScan = hasBaseline ? DateTime.Now.ToString("MMM d, yyyy HH:mm") : "Never scanned";
        DashboardExecutiveSummary = hasBaseline
            ? $"{DashboardOpenIssues:N0} findings detected; {DashboardAutoFixCount:N0} safe actions are ready. The guided workflow recommends {CurrentJourneyStepTitle.ToLowerInvariant()}."
            : "No baseline exists yet. Start optimization to analyze SEO, content, links, taxonomy, performance and accessibility.";

        BuildDashboardPriorities();
        UpdateSafeAutopilotReadiness();

        if (!analyzed)
        {
            CurrentJourneyStepTitle = "Step 1 · Analyze website";
            CurrentJourneyStepDescription = "Create the first measurable SEO baseline across content, links and taxonomy.";
            CurrentJourneyActionLabel = "Start analysis";
            CurrentJourneyTarget = "SEO Audit";
        }
        else if (!reviewed)
        {
            CurrentJourneyStepTitle = "Step 2 · AI review";
            CurrentJourneyStepDescription = "Convert the completed audits into concrete actions with impact, confidence and risk.";
            CurrentJourneyActionLabel = "Open AI review";
            CurrentJourneyTarget = "Suggested Changes";
        }
        else if (!previewed)
        {
            CurrentJourneyStepTitle = "Step 3 · Preview changes";
            CurrentJourneyStepDescription = "Inspect current and proposed values before any approval or WordPress write.";
            CurrentJourneyActionLabel = "Preview proposed changes";
            CurrentJourneyTarget = "Suggested Changes";
        }
        else if (!approved)
        {
            CurrentJourneyStepTitle = "Step 4 · Approve plan";
            CurrentJourneyStepDescription = "Approve safe changes and leave protected or high-risk work blocked.";
            CurrentJourneyActionLabel = "Review approvals";
            CurrentJourneyTarget = "Approval Queue";
        }
        else if (!executed)
        {
            CurrentJourneyStepTitle = "Step 5 · Execute safely";
            CurrentJourneyStepDescription = "Create backups, write supported changes to WordPress and retain rollback points.";
            CurrentJourneyActionLabel = "Open execution plan";
            CurrentJourneyTarget = "Execution Center";
        }
        else if (!verified)
        {
            CurrentJourneyStepTitle = "Step 6 · Verify results";
            CurrentJourneyStepDescription = "Confirm saved WordPress values and review before/after evidence.";
            CurrentJourneyActionLabel = "Verify execution";
            CurrentJourneyTarget = "Evidence Center";
        }
        else
        {
            CurrentJourneyStepTitle = "Step 7 · Complete and track";
            CurrentJourneyStepDescription = "Review the final score, transaction history, evidence and recovery points.";
            CurrentJourneyActionLabel = "Open optimization history";
            CurrentJourneyTarget = "Transaction Center";
        }

        HealthChart.Clear();
        HealthChart.Add(new DashboardChartItem("SEO", seoScore, "Search visibility", "🔎", "#D7A900", "#FFF0A8"));
        HealthChart.Add(new DashboardChartItem("Content", contentScore, "Quality and structure", "✎", "#2FBF71", "#A8F0C7"));
        HealthChart.Add(new DashboardChartItem("Links", linkScore, "Healthy destinations", "↗", "#4C8DFF", "#B8D1FF"));
        HealthChart.Add(new DashboardChartItem("Categories", categoryScore, "Taxonomy health", "▦", "#DD5B5B", "#FFC0C0"));

        RecentActivity.Clear();
        RecentActivity.Add(new DashboardActivityItem("AI actions", DashboardAutoFixCount == 0 ? "No directly executable actions yet" : $"{DashboardAutoFixCount} actions ready for direct execution", "AI", "Suggested Changes"));
        RecentActivity.Add(new DashboardActivityItem("SEO audit", SeoAudit.AuditedItems == 0 ? "Run the first measurable audit" : $"{SeoAudit.AuditedItems} items audited · score {seoScore}", "SEO", "SEO Audit"));
        RecentActivity.Add(new DashboardActivityItem("Content audit", ContentAudit.AuditedItems == 0 ? "Content snapshot has not been audited" : $"{ContentAudit.AuditedItems} items audited · score {contentScore}", "DOC", "Content Audit"));
        RecentActivity.Add(new DashboardActivityItem("Link health", linkTotal == 0 ? "Start a broken-link scan" : $"{BrokenLinks.HealthyCount} healthy · {BrokenLinks.BrokenCount} broken", "URL", "Broken Links"));
        foreach (var job in Jobs.Items.OrderByDescending(x => x.UpdatedAtUtc).Take(3))
        {
            RecentActivity.Add(new DashboardActivityItem(
                $"{job.JobType} • {job.Status}",
                $"{job.CurrentStep} · {job.UpdatedAtUtc.ToLocalTime():HH:mm:ss}",
                job.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? "ERR" : "JOB",
                "Jobs"));
        }
    }

    private void BuildDashboardPriorities()
    {
        DashboardPriorities.Clear();

        void Add(string title, string detail, int gain, string severity, string destination, int count)
        {
            if (count <= 0) return;
            DashboardPriorities.Add(new DashboardPriorityAction(title, detail, $"+{gain} pts", severity, destination, count));
        }

        Add("Fix critical SEO findings", "Titles, metadata, indexability and heading issues with the highest search impact.", Math.Min(8, Math.Max(2, SeoAudit.Issues.Count / 8)), "HIGH", "SEO Audit", SeoAudit.Issues.Count);
        Add("Improve content quality", "Thin, incomplete or poorly structured content detected by the content audit.", Math.Min(7, Math.Max(2, ContentAudit.Issues.Count / 10)), "HIGH", "Content Audit", ContentAudit.Issues.Count);
        Add("Repair broken links", "Restore crawl paths and user navigation by resolving broken URLs.", Math.Min(5, Math.Max(1, BrokenLinks.BrokenCount / 4)), "MEDIUM", "Broken Links", BrokenLinks.BrokenCount);
        Add("Execute safe AI actions", "Low-risk supported changes are ready for backup, execution and verification.", Math.Min(10, Math.Max(2, DashboardAutoFixCount / 5)), "READY", "Execution Center", DashboardAutoFixCount);
        Add("Review protected actions", "Medium or high-risk actions need approval, staging or a specialist adapter.", Math.Min(4, Math.Max(1, DashboardReviewCount / 8)), "REVIEW", "Approval Queue", DashboardReviewCount + DashboardManualCount);

        if (DashboardPriorities.Count == 0)
            DashboardPriorities.Add(new DashboardPriorityAction("Run website analysis", "Create the first measurable baseline and AI action plan.", "+0 pts", "START", "SEO Audit", 0));
    }

    private static void SetJourneyState(bool complete, bool current, out string state, out Brush brush)
    {
        if (complete)
        {
            state = "COMPLETED";
            brush = Brushes.SeaGreen;
            return;
        }

        if (current)
        {
            state = "CURRENT";
            brush = Brushes.DarkOrange;
            return;
        }

        state = "NOT STARTED";
        brush = Brushes.IndianRed;
    }

    private void UpdateRuntimeMetrics()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var processorTime = process.TotalProcessorTime;
            var elapsedMs = Math.Max(1d, (now - _lastProcessorSampleUtc).TotalMilliseconds);
            var cpuMs = Math.Max(0d, (processorTime - _lastProcessorTime).TotalMilliseconds);
            DashboardCpuPercent = Math.Round(Math.Clamp(cpuMs / (elapsedMs * Environment.ProcessorCount) * 100d, 0d, 100d), 1);
            DashboardMemoryMb = Math.Max(0, process.WorkingSet64 / 1024 / 1024);
            SystemMemoryUsagePercent = GetSystemMemoryUsagePercent();
            UpdateMemoryCoolingState();
            _lastProcessorTime = processorTime;
            _lastProcessorSampleUtc = now;

            var databasePath = _applicationPaths.GetDatabasePath();
            if (File.Exists(databasePath))
            {
                var bytes = new FileInfo(databasePath).Length;
                DashboardDatabaseSize = bytes >= 1024L * 1024L
                    ? $"{bytes / 1024d / 1024d:N1} MB"
                    : $"{Math.Max(1, bytes / 1024d):N0} KB";
            }
            else
            {
                DashboardDatabaseSize = "Not created";
            }
        }
        catch
        {
            DashboardWorkerState = "Metrics delayed";
        }
    }

    private void UpdateDashboardLastSiteSync()
    {
        if (Sites.SelectedSite is null)
        {
            DashboardLastSiteSync = "No site selected";
            return;
        }

        var syncedAt = Explorer.LoadedAt;
        DashboardLastSiteSync = syncedAt is null || syncedAt == DateTimeOffset.MinValue
            ? "Never synchronized"
            : syncedAt.Value.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    }

    private async Task UpdateLiveDashboardAsync()
    {
        DashboardLiveClock = DateTime.Now.ToString("HH:mm:ss");
        DashboardPulseOn = !DashboardPulseOn;
        _liveDashboardTick++;
        UpdateRuntimeMetrics();
        DashboardSelectedSite = Sites.SelectedSite?.Name ?? "No site selected";

        DashboardExecutionProgress = ExecutionCenter.IsBusy ? ExecutionCenter.ProgressPercent : 0;
        DashboardExecutionStep = ExecutionCenter.IsBusy
            ? $"{ExecutionCenter.QueueState} • {ExecutionCenter.CurrentStep}"
            : "Execution queue idle";

        // While Windows is under memory pressure, keep only the lightweight clock/metrics
        // updates alive and pause database-heavy dashboard refreshes until usage settles.
        if (IsMemoryCooling || _liveDashboardTickBusy || _liveDashboardTick % 3 != 0) return;
        _liveDashboardTickBusy = true;
        try
        {
            await Jobs.LoadAsync();
            DashboardRunningJobs = Jobs.RunningCount;
            DashboardCompletedJobs = Jobs.CompletedCount;
            DashboardFailedJobs = Jobs.FailedCount;
            DashboardQueueTotal = Jobs.Items.Count;
            DashboardWorkerState = DashboardRunningJobs > 0 ? "Processing" : DashboardFailedJobs > 0 ? "Attention" : "Idle";
            var latestJob = Jobs.Items.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefault();
            DashboardLastJob = latestJob is null
                ? "No jobs recorded"
                : $"{latestJob.JobType} • {latestJob.Status} • {latestJob.UpdatedAtUtc.ToLocalTime():HH:mm:ss}";
            DashboardLiveStatus = DashboardRunningJobs > 0
                ? $"LIVE • {DashboardRunningJobs} job(s) running"
                : DashboardFailedJobs > 0
                    ? $"LIVE • {DashboardFailedJobs} job(s) need attention"
                    : "LIVE • systems ready";
            DashboardLastRefresh = $"Updated {DateTime.Now:HH:mm:ss}";
            RefreshDashboard();
        }
        catch
        {
            DashboardLiveStatus = "LIVE • refresh delayed";
        }
        finally
        {
            _liveDashboardTickBusy = false;
        }
    }

    private void UpdateMemoryCoolingState()
    {
        // Enter at 80%, leave only after the machine drops below 72%.
        // The hysteresis prevents rapid on/off flashing around the threshold.
        var shouldCool = IsMemoryCooling
            ? SystemMemoryUsagePercent >= 72d
            : SystemMemoryUsagePercent >= 80d;

        if (shouldCool)
        {
            IsMemoryCooling = true;
            MemoryCoolingStatus = $"Cooling memory… {SystemMemoryUsagePercent:N0}% in use";
            DashboardWorkerState = "Cooling";
            _ = RunMemoryCoolingActionsAsync();
            return;
        }

        if (IsMemoryCooling)
        {
            IsMemoryCooling = false;
            MemoryCoolingStatus = $"Memory stabilized at {SystemMemoryUsagePercent:N0}%";
            DashboardWorkerState = DashboardRunningJobs > 0 ? "Processing" : "Idle";
        }
        else
        {
            MemoryCoolingStatus = $"Memory stable • {SystemMemoryUsagePercent:N0}% used";
        }
    }

    private async Task RunMemoryCoolingActionsAsync()
    {
        if (_memoryCoolingActionRunning ||
            DateTime.UtcNow - _lastMemoryCoolingActionUtc < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _memoryCoolingActionRunning = true;
        _lastMemoryCoolingActionUtc = DateTime.UtcNow;
        try
        {
            await ReleaseApplicationMemoryAsync(aggressive: false);
        }
        finally
        {
            _memoryCoolingActionRunning = false;
        }
    }

    private async Task CleanDeviceMemoryAsync()
    {
        if (IsMemoryCleanupRunning) return;
        IsMemoryCleanupRunning = true;
        CleanDeviceMemoryCommand.NotifyCanExecuteChanged();
        MemoryCleanupStatus = "Releasing hidden grids, managed objects, and unused working-set pages…";
        var before = Process.GetCurrentProcess().WorkingSet64;
        try
        {
            await ReleaseApplicationMemoryAsync(aggressive: true);
            await Task.Delay(250);
            using var process = Process.GetCurrentProcess();
            var after = process.WorkingSet64;
            var released = Math.Max(0, before - after);
            DashboardMemoryMb = Math.Max(0, after / 1024 / 1024);
            LastMemoryCleanupResult = released > 0
                ? $"Released approximately {released / 1024d / 1024d:N1} MB from this application"
                : "Cleanup completed. Windows kept the current working set because it is still active.";
            MemoryCleanupStatus = $"Completed at {DateTime.Now:HH:mm:ss} • app memory {DashboardMemoryMb:N0} MB";
            await _dialogService.ShowInformationAsync(
                "Memory cleanup completed",
                LastMemoryCleanupResult + Environment.NewLine + Environment.NewLine + "This safe cleanup releases unused memory owned by AI WordPress Manager. It does not close or alter other applications.");
        }
        catch (Exception ex)
        {
            MemoryCleanupStatus = "Memory cleanup failed";
            LastMemoryCleanupResult = ex.Message;
            await _dialogService.ShowErrorAsync("Memory cleanup failed", ex.ToString());
        }
        finally
        {
            IsMemoryCleanupRunning = false;
            CleanDeviceMemoryCommand.NotifyCanExecuteChanged();
        }
    }

    private static async Task ReleaseApplicationMemoryAsync(bool aggressive)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            await dispatcher.InvokeAsync(PagedDataGridBehavior.ReleaseHiddenGridCaches, DispatcherPriority.ContextIdle);
        }

        if (aggressive)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        else
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
        }

        using var process = Process.GetCurrentProcess();
        _ = EmptyWorkingSet(process.Handle);
    }

    private static double GetSystemMemoryUsagePercent()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status) || status.TotalPhysical == 0)
        {
            return 0d;
        }

        var used = status.TotalPhysical - status.AvailablePhysical;
        return Math.Round(Math.Clamp(used * 100d / status.TotalPhysical, 0d, 100d), 1);
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private async Task RefreshCurrentPageAsync()
    {
        switch (CurrentPage)
        {
            case "Dashboard": RefreshDashboard(); break;
            case "Sites": await LoadSafelyAsync("Sites", Sites.LoadAsync); break;
            case "WordPress Explorer": await LoadSafelyAsync("WordPress Explorer", Explorer.LoadAsync); break;
            case "Content Audit": await LoadSafelyAsync("Content Audit", ContentAudit.LoadAsync); break;
            case "SEO Audit": await LoadSafelyAsync("SEO Audit", SeoAudit.LoadAsync); break;
            case "SEO History": await LoadSafelyAsync("SEO History", SeoAudit.LoadAsync); break;
            case "Broken Links": await LoadSafelyAsync("Broken Links", BrokenLinks.LoadAsync); break;
            case "Category Planner": await LoadSafelyAsync("Category Planner", CategoryPlanner.LoadAsync); break;
            case "Internal Links": await LoadSafelyAsync("Internal Links", InternalLinks.LoadAsync); break;
            case "Suggested Changes": await LoadSafelyAsync("Suggested Changes", SuggestedChanges.ShowAllAsync); break;
            case "Approval Queue": await LoadSafelyAsync("Approval Queue", SuggestedChanges.ShowApprovalQueueAsync); break;
            case "Settings": await LoadSafelyAsync("Settings", Settings.LoadAsync); break;
            case "AI Studio": await LoadSafelyAsync("AI Studio", AiStudio.LoadAsync); break;
            case "Action Center": await LoadSafelyAsync("Action Center", ActionCenter.LoadAsync); break;
            case "Deletion Center": await LoadSafelyAsync("Deletion Center", DeletionCenter.LoadAsync); break;
            case "Theme Inspector": await LoadSafelyAsync("Theme Inspector", ThemeInspector.LoadOfflineAsync); break;
            case "Post SEO Editor": await LoadSafelyAsync("Post SEO Editor", PostEditor.LoadOfflineAsync); break;
            case "Execution Center": await LoadSafelyAsync("Execution Center", ExecutionCenter.LoadAsync); break;
            case "Jobs": await LoadSafelyAsync("Jobs", Jobs.LoadAsync); break;
            case "Notification Center": await LoadSafelyAsync("Notification Center", Jobs.LoadAsync); Jobs.MarkNotificationsRead(); break;
            case "Activity Timeline": RefreshDashboard(); await LoadSafelyAsync("Activity Timeline", Jobs.LoadAsync); break;
            case "Visual Inspector": await LoadSafelyAsync("Visual Inspector", VisualInspector.LoadAsync); break;
            case "Visual WordPress Editor": await LoadSafelyAsync("Visual WordPress Editor", VisualEditor.LoadAsync); break;
            case "AI Site Brain": await LoadSafelyAsync("AI Site Brain", SiteBrain.LoadAsync); break;
            case "AI Autopilot": await LoadSafelyAsync("AI Autopilot", Orchestrator.LoadAsync); break;
            case "Evidence Center": await LoadSafelyAsync("Evidence Center", EvidenceCenter.LoadAsync); break;
            case "Scheduler Center": await LoadSafelyAsync("Scheduler Center", SchedulerCenter.LoadAsync); break;
            case "AI Decision Center": await LoadSafelyAsync("AI Decision Center", DecisionCenter.LoadAsync); break;
            case "Transaction Center": await LoadSafelyAsync("Transaction Center", TransactionCenter.LoadAsync); break;
            case "Operations Center": await LoadSafelyAsync("Operations Center", OperationsCenter.LoadAsync); break;
            case "Release Readiness": await LoadSafelyAsync("Release Readiness", ReleaseReadiness.LoadAsync); break;
            case "Plugin Compatibility": await LoadSafelyAsync("Plugin Compatibility", PluginCompatibility.LoadAsync); break;
            case "Health Center": await LoadSafelyAsync("Health Center", HealthCenter.LoadAsync); break;
            case "Backups": await LoadSafelyAsync("Backups", Backups.LoadAsync); break;
            case "Reports": await LoadSafelyAsync("Reports", Reports.LoadAsync); break;
            case "Logs": await LoadSafelyAsync("Logs", Logs.LoadAsync); break;
            case "Performance": UpdateRuntimeMetrics(); break;
        }
    }

    private async Task AddSiteAsync()
    {
        await NavigateAsync("Sites");
        Sites.Wizard.Open();
    }

    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        IsDarkTheme = _themeService.IsDarkTheme;
        ThemeIcon = IsDarkTheme ? "☀" : "☾";
    }

    private void ApplyAccentPalette(string? paletteName)
    {
        if (string.IsNullOrWhiteSpace(paletteName)) return;
        _themeService.ApplyAccentPalette(paletteName);
        CurrentPaletteName = _themeService.CurrentPalette;
    }


    private void ApplyFontPalette(string? paletteName)
    {
        if (string.IsNullOrWhiteSpace(paletteName)) return;
        _themeService.ApplyFontPalette(paletteName);
        CurrentFontPaletteName = _themeService.CurrentFontPalette;
    }

    private void UpdatePageMetadata(string page)
    {
        var ar = _localization.IsArabic;
        PageTitle = ar ? page switch
        {
            "Dashboard" => "لوحة التحكم", "Sites" => "المواقع", "WordPress Explorer" => "مستكشف ووردبريس",
            "Content Audit" => "تدقيق المحتوى", "SEO Audit" => "تدقيق تحسين محركات البحث", "SEO History" => "سجل تحسين محركات البحث", "Broken Links" => "الروابط المعطلة",
            "Category Planner" => "مخطط التصنيفات", "Content Planner" => "مخطط المحتوى", "Article Generator" => "منشئ المقالات", "Internal Links" => "الروابط الداخلية", "Suggested Changes" => "التغييرات المقترحة",
            "Approval Queue" => "قائمة الموافقات", "Settings" => "الإعدادات", "AI Studio" => "استوديو الذكاء الاصطناعي",
            "Action Center" => "مركز الإجراءات", "Release Readiness" => "جاهزية الإصدار", "AI Autopilot" => "الطيار الآلي الذكي", "Evidence Center" => "مركز الأدلة", "Scheduler Center" => "جدولة الأتمتة", "AI Decision Center" => "محرك قرارات الذكاء الاصطناعي", "Transaction Center" => "مركز معاملات ووردبريس", "Visual Inspector" => "المعاين البصري", "Visual WordPress Editor" => "محرر ووردبريس البصري", "AI Site Brain" => "ذاكرة الموقع الذكية", "Jobs" => "المهام",
            "Backups" => "النسخ الاحتياطية", "Reports" => "التقارير", "Logs" => "السجلات", "Help" => "دليل المستخدم والاختصارات", _ => page
        } : page;

        PageDescription = ar ? page switch
        {
            "Dashboard" => "راقب صحة الموقع والمهام المعلقة والنشاط الأخير.",
            "AI Studio" => "اختبر مزودي الذكاء الاصطناعي وأنشئ اقتراحًا دقيقًا قبل تطبيقه.",
            "Visual Inspector" => "افحص الصفحة بصريًا عبر أحجام الشاشات واكتشف مشاكل التصميم وقابلية الاستخدام.",
            "Visual WordPress Editor" => "حدد أي عنصر من الصفحة الحية وعاين تعديل CSS مع صور قبل وبعد دون الكتابة إلى ووردبريس قبل الموافقة.",
            "AI Site Brain" => "احفظ أسلوب الموقع وقواعد المحتوى والتصميم لكي تكون اقتراحات الذكاء الاصطناعي مخصصة.",
            "AI Autopilot" => "شغّل دورة موحدة للتحليل والتخطيط والموافقة والتنفيذ والتحقق وفق سياسة الموقع.",
            "Evidence Center" => "راجع صور قبل وبعد وملفات التحقق وأدلة التنفيذ والاسترجاع في مكان واحد.",
            "Scheduler Center" => "جدول عمليات التدقيق والنسخ الاحتياطي ودورات الذكاء الاصطناعي لكل موقع.",
            "Settings" => "اضبط اللغة والمظهر والتخزين ومزودي الذكاء الاصطناعي والأتمتة.",
            "Jobs" => "تابع العمليات الخلفية ونسب التقدم والإلغاء وإعادة المحاولة.",
            "Help" => "افتح دليل المستخدم الكامل وراجع اختصارات أهم وظائف التطبيق.",
            _ => "تعرض هذه الشاشة البيانات المحفوظة محليًا وتدعم سير العمل الآمن قبل التنفيذ."
        } : page switch
        {
            "Sites" => "Connect and manage authorized WordPress websites.",
            "WordPress Explorer" => "Read recent posts, pages and categories from the selected WordPress site.",
            "SEO Audit" => "Review measurable SEO issues from the synchronized WordPress snapshot.",
            "Broken Links" => "Check content links safely and store the latest scan results locally.",
            "Category Planner" => "Analyze category health from the offline SQLite snapshot.",
            "Internal Links" => "Generate internal-link suggestions locally without changing WordPress.",
            "Suggested Changes" => "Preview and approve proposed changes before execution.",
            "Deletion Center" => "Preview dependencies, back up data, move content to Trash, and permanently delete only with explicit safety controls.",
            "Theme Inspector" => "Discover the active WordPress theme and design capabilities without modifying theme files.",
            "Visual Inspector" => "Capture responsive screenshots, compare layouts, and prepare visual design findings for safe review.",
            "Visual WordPress Editor" => "Inspect the live page, preview exact CSS changes, capture evidence, and prepare an approved visual execution proposal.",
            "AI Autopilot" => "Orchestrate discovery, audits, AI planning, approvals, safe execution, verification, evidence, and recovery as one resumable workflow.",
            "AI Decision Center" => "Evaluate every proposed change against risk, approval, staging, executor, verification, and rollback policy before WordPress execution.",
            "Transaction Center" => "Review the append-only WordPress transaction journal, detect interrupted writes, export audit history, and start a safe reconciliation workflow.",
            "Evidence Center" => "Review before/after screenshots, verification artifacts, execution evidence, and recovery files in one workspace.",
            "Scheduler Center" => "Schedule per-site AI workflows, audits, link scans, and verified database backups.",
            "Post SEO Editor" => "Edit WordPress post and page settings with backups, confirmation, and measurable SEO guidance.",
            "Execution Center" => "Execute approved concrete changes in bulk with backup, verification, cancellation, and rollback.",
            "Jobs" => "Track synchronization and background operations with progress and cancellation.",
            "Backups" => "Review verified local database and website backups.",
            "Reports" => "Build executive, SEO, content, design, and change-history reports.",
            "Logs" => "Inspect application, WordPress, AI, execution, and synchronization events.",
            "Help" => "Open the bundled Word user guide and review keyboard shortcuts.",
            "Design Audit" => "Review visual consistency, typography, spacing, color, and component issues.",
            "Responsive Audit" => "Compare desktop, tablet, and mobile layouts with visual evidence.",
            "Performance" => "Review page speed, requests, asset weight, and Core Web Vitals signals.",
            "Accessibility" => "Review contrast, labels, keyboard support, headings, and touch targets.",
            "Content Planner" => "Build a prioritized content calendar from topic clusters and content gaps.",
            "Article Generator" => "Create reviewed WordPress drafts with SEO metadata and internal links.",
            "SEO History" => "Review the selected site SEO score and issue trend across saved audits.",
            "Settings" => "Configure language, theme, storage, AI, and automation preferences.",
            "AI Studio" => "Test enabled AI providers and generate an exact proposal before using them on WordPress.",
            "AI Site Brain" => "Store site-specific language, brand, content, SEO, and design preferences for future AI recommendations.",
            "Action Center" => "Apply safe fixes, review approvals, retry failures, and access rollback from one operational workspace.",
            "Release Readiness" => "Validate source structure, XAML resources, WordPress Bridge packaging, documentation, and installer readiness before Release.",
            _ => page == "Dashboard" ? "Monitor website health, pending work, and recent activity." : "This module is prepared for a later phase."
        };
    }

    private void ToggleLanguage()
    {
        var toArabic = FlowDirection == FlowDirection.LeftToRight;
        FlowDirection = toArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageLabel = toArabic ? "EN" : "AR";
        CultureInfo.CurrentUICulture = toArabic ? new CultureInfo("ar-KW") : new CultureInfo("en-US");
        if (toArabic) _localization.ApplyArabic(); else _localization.ApplyEnglish();
        UpdatePageMetadata(CurrentPage);
    }

    private async Task ShowNotificationsAsync()
    {
        await NavigateAsync("Notification Center");
        Jobs.MarkNotificationsRead();
    }

    private void RaisePageVisibility()
    {
        OnPropertyChanged(nameof(IsDashboardVisible));
        OnPropertyChanged(nameof(IsSitesVisible));
        OnPropertyChanged(nameof(IsExplorerVisible));
        OnPropertyChanged(nameof(IsJobsVisible));
        OnPropertyChanged(nameof(IsNotificationCenterVisible));
        OnPropertyChanged(nameof(IsActivityTimelineVisible));
        OnPropertyChanged(nameof(IsVisualInspectorVisible));
        OnPropertyChanged(nameof(IsVisualEditorVisible));
        OnPropertyChanged(nameof(IsSiteBrainVisible));
        OnPropertyChanged(nameof(IsOrchestratorVisible));
        OnPropertyChanged(nameof(IsEvidenceCenterVisible));
        OnPropertyChanged(nameof(IsSchedulerCenterVisible));
        OnPropertyChanged(nameof(IsDecisionCenterVisible));
        OnPropertyChanged(nameof(IsTransactionCenterVisible));
        OnPropertyChanged(nameof(IsOperationsCenterVisible));
        OnPropertyChanged(nameof(IsReleaseReadinessVisible));
        OnPropertyChanged(nameof(IsPluginCompatibilityVisible));
        OnPropertyChanged(nameof(IsHealthCenterVisible));
        OnPropertyChanged(nameof(IsBackupsVisible));
        OnPropertyChanged(nameof(IsReportsVisible));
        OnPropertyChanged(nameof(IsLogsVisible));
        OnPropertyChanged(nameof(IsHelpVisible));
        OnPropertyChanged(nameof(IsPerformanceVisible));
        OnPropertyChanged(nameof(IsContentAuditVisible));
        OnPropertyChanged(nameof(IsSeoAuditVisible));
        OnPropertyChanged(nameof(IsSeoHistoryVisible));
        OnPropertyChanged(nameof(IsBrokenLinksVisible));
        OnPropertyChanged(nameof(IsCategoryPlannerVisible));
        OnPropertyChanged(nameof(IsContentPlannerVisible));
        OnPropertyChanged(nameof(IsArticleGeneratorVisible));
        OnPropertyChanged(nameof(IsInternalLinksVisible));
        OnPropertyChanged(nameof(IsSuggestedChangesVisible));
        OnPropertyChanged(nameof(IsApprovalQueueVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(IsAiStudioVisible));
        OnPropertyChanged(nameof(IsActionCenterVisible));
        OnPropertyChanged(nameof(IsDeletionCenterVisible));
        OnPropertyChanged(nameof(IsThemeInspectorVisible));
        OnPropertyChanged(nameof(IsPostEditorVisible));
        OnPropertyChanged(nameof(IsExecutionCenterVisible));
        OnPropertyChanged(nameof(IsPlaceholderVisible));
    }
}

public sealed class DashboardChartItem
{
    public DashboardChartItem(string label, int score, string description, string icon, string color, string glowColor)
    {
        Label = label;
        Score = score;
        Description = description;
        Icon = icon;
        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        GlowFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(glowColor));
    }

    public string Label { get; }
    public int Score { get; }
    public string Description { get; }
    public string Icon { get; }
    public Brush Fill { get; }
    public Brush GlowFill { get; }
    public double BarHeight => Math.Max(8, Score * 1.45);
}


public sealed record DashboardActivityItem(string Title, string Description, string Badge, string Destination);

public sealed record DashboardPriorityAction(string Title, string Detail, string Gain, string Severity, string Destination, int Count);
