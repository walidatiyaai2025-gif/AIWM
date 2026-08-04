using System.Windows;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Infrastructure;
using AIWordPressManager.Persistence;
using AIWordPressManager.WordPress;
using AIWordPressManager.Desktop.ViewModels.Sites;
using AIWordPressManager.Desktop.Validators;
using AIWordPressManager.Automation.Jobs;
using AIWordPressManager.Automation.Visual;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AIWordPressManager.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        IProgress<StartupProgress> progress = new Progress<StartupProgress>(splash.Report);
        splash.Show();
        var splashStartedAt = DateTime.UtcNow;
        progress.Report(StartupProgress.Create(3, "Starting application", "Initializing the desktop runtime"));

        try
        {
            progress.Report(StartupProgress.Create(10, "Building services", "Preparing dependency injection, logging, and configuration"));
            _host = CreateHostBuilder(e.Args).Build();

            progress.Report(StartupProgress.Create(20, "Starting background services", "Starting the scheduler and application services"));
            await _host.StartAsync();
            RegisterGlobalExceptionHandlers();

            progress.Report(StartupProgress.Create(30, "Checking application folders", "Verifying logs, backups, documentation, and setup paths"));
            await RunStartupHealthChecksAsync(_host.Services, progress);

            progress.Report(StartupProgress.Create(36, "Preparing SQLite", "Creating or upgrading the local database"));
            using (var scope = _host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializationService>().InitializeAsync();
            }

            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            await mainViewModel.InitializeAsync(progress);

            progress.Report(StartupProgress.Create(96, "Creating main window", "Applying theme, navigation, and the active site context"));
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            // Do not keep the splash open while non-essential screens hydrate.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await mainViewModel.LoadDeferredSiteDataAsync();
                }
                catch (Exception deferredException)
                {
                    Log.Warning(deferredException, "Deferred workspace loading failed after startup.");
                }
            }, System.Windows.Threading.DispatcherPriority.Background);

            progress.Report(StartupProgress.Create(100, "Ready", "AI WordPress Website Manager is ready"));
            var automationSettings = await _host.Services.GetRequiredService<AIWordPressManager.Application.Settings.IApplicationSettingsService>().GetAiAutomationSettingsAsync();
            var minimumSplash = TimeSpan.FromSeconds(Math.Max(3, automationSettings.MinimumSplashSeconds));
            var remainingSplash = minimumSplash - (DateTime.UtcNow - splashStartedAt);
            if (remainingSplash > TimeSpan.Zero) await Task.Delay(remainingSplash);
            splash.Close();
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _ = WriteStartupCompletedLogAsync(_host.Services);
        }
        catch (Exception exception)
        {
            progress.Report(StartupProgress.Create(100, "Startup failed", exception.Message));
            Log.Fatal(exception, "Application startup failed.");
            _host?.Services.GetService<GlobalErrorPresenter>()?.Show(exception, "Startup");
            try { splash.Close(); } catch { }
            Shutdown(-1);
        }
    }

    private static async Task RunStartupHealthChecksAsync(IServiceProvider services, IProgress<StartupProgress> progress)
    {
        var paths = services.GetRequiredService<IApplicationPathService>();
        var checks = new (string Name, Func<string> Resolve)[]
        {
            ("Logs", paths.GetLogsDirectory),
            ("Backups", paths.GetBackupsDirectory),
            ("Application data", paths.GetApplicationDataDirectory)
        };

        for (var index = 0; index < checks.Length; index++)
        {
            var path = checks[index].Resolve();
            System.IO.Directory.CreateDirectory(path);
            progress.Report(StartupProgress.Create(31 + index, "Checking application folders", $"{checks[index].Name}: {path}"));
            await Task.Yield();
        }
    }

    private static async Task WriteStartupCompletedLogAsync(IServiceProvider services)
    {
        try
        {
            var paths = services.GetRequiredService<IApplicationPathService>();
            var logPath = System.IO.Path.Combine(paths.GetLogsDirectory(), "startup-history.log");
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var line = $"{DateTimeOffset.Now:O} | READY | PID={Environment.ProcessId} | WorkingSetMB={process.WorkingSet64 / 1024d / 1024d:N1}{Environment.NewLine}";
            await System.IO.File.AppendAllTextAsync(logPath, line);
        }
        catch
        {
            // Startup telemetry must never block the application.
        }
    }


    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _host?.Services.GetRequiredService<GlobalErrorPresenter>().Show(args.Exception, "WPF Dispatcher");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Dispatcher.Invoke(() => _host?.Services.GetRequiredService<GlobalErrorPresenter>().Show(args.Exception, "Background Task"));
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Dispatcher.Invoke(() => _host?.Services.GetRequiredService<GlobalErrorPresenter>().Show(ex, "AppDomain"));
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try { ProcessTreeCleanup.KillDescendantsOfCurrentProcess(); } catch { }
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.SetBasePath(AppContext.BaseDirectory);
            configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            configuration.AddEnvironmentVariables();
        })
        .UseSerilog((context, services, logger) =>
        {
            var paths = services.GetRequiredService<IApplicationPathService>();
            logger.ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.File(System.IO.Path.Combine(paths.GetLogsDirectory(), "application-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
        })
        .ConfigureServices(services =>
        {
            services.AddInfrastructure();
            services.AddPersistence();
            AIWordPressManager.WordPress.DependencyInjection.AddWordPress(services);
            AIWordPressManager.AI.DependencyInjection.AddAi(services);
            services.AddSingleton<AIWordPressManager.Desktop.Services.Sites.ICurrentSiteContext, AIWordPressManager.Desktop.Services.Sites.CurrentSiteContext>();
            services.AddSingleton<AddSiteWizardValidator>();
            services.AddTransient<AddSiteWizardViewModel>();
            services.AddSingleton<SitesViewModel>();
            services.AddSingleton<WordPressExplorerViewModel>();
            services.AddSingleton<ContentAuditViewModel>();
            services.AddSingleton<SeoAuditViewModel>();
            services.AddSingleton<BrokenLinksViewModel>();
            services.AddSingleton<CategoryPlannerViewModel>();
            services.AddSingleton<ContentPlannerViewModel>();
            services.AddSingleton<ArticleGeneratorViewModel>();
            services.AddSingleton<InternalLinksViewModel>();
            services.AddSingleton<SuggestedChangesViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AiStudioViewModel>();
            services.AddSingleton<ActionCenterViewModel>();
            services.AddSingleton<DeletionCenterViewModel>();
            services.AddSingleton<ThemeInspectorViewModel>();
            services.AddSingleton<PostSeoEditorViewModel>();
            services.AddSingleton<ExecutionCenterViewModel>();
            services.AddSingleton<JobsViewModel>();
            services.AddSingleton<VisualInspectionService>();
            services.AddSingleton<VisualInspectorViewModel>();
            services.AddSingleton<VisualWordPressEditorViewModel>();
            services.AddSingleton<SiteBrainViewModel>();
            services.AddSingleton<AutopilotOrchestratorViewModel>();
            services.AddSingleton<EvidenceCenterViewModel>();
            services.AddSingleton<SchedulerCenterViewModel>();
            services.AddSingleton<AiDecisionCenterViewModel>();
            services.AddSingleton<TransactionCenterViewModel>();
            services.AddSingleton<OperationsCenterViewModel>();
            services.AddSingleton<ReleaseReadinessViewModel>();
            services.AddSingleton<PluginCompatibilityCenterViewModel>();
            services.AddSingleton<HealthCenterViewModel>();
            services.AddSingleton<BackupsViewModel>();
            services.AddSingleton<ReportsViewModel>();
            services.AddSingleton<LogsViewModel>();
            services.AddSingleton<HelpViewModel>();
            services.AddHostedService<ScheduledWordPressSyncService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<AiErrorAdvisorService>();
            services.AddSingleton<GlobalErrorPresenter>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<UiOperationService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
        });
}
