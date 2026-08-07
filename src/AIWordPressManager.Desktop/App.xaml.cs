using System.Collections.Concurrent;
using System.Windows;
using AIWordPressManager.AI;
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
    private static readonly TimeSpan GlobalErrorRepeatWindow = TimeSpan.FromSeconds(10);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentGlobalErrors = new(StringComparer.Ordinal);
    private IHost? _host;
    private bool _hostStarted;

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
            RegisterGlobalExceptionHandlers();

            progress.Report(StartupProgress.Create(20, "Preparing application services", "Background services will start after the workspace is visible"));

            progress.Report(StartupProgress.Create(30, "Checking application folders", "Verifying logs, backups, documentation, and setup paths"));
            await RunStartupHealthChecksAsync(_host.Services, progress);

            progress.Report(StartupProgress.Create(36, "Preparing SQLite", "Creating or upgrading the local database"));
            using (var scope = _host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IDatabaseInitializationService>().InitializeAsync();
            }

            progress.Report(StartupProgress.Create(42, "Securing workspace", "Loading users, roles, and permissions"));
            var paths = _host.Services.GetRequiredService<IApplicationPathService>();
            var databasePath = paths.GetDatabasePath();
            await UserSecurityStore.EnsureCreatedAsync(databasePath);

            splash.Hide();
            var loginWindow = new SystemLoginWindow(databasePath);
            var loginAccepted = loginWindow.ShowDialog() == true && loginWindow.AuthenticatedUser is not null;
            if (!loginAccepted)
            {
                try { loginWindow.Close(); } catch { }
                try { splash.Close(); } catch { }
                Shutdown(0);
                return;
            }

            SystemSecuritySession.SetAuthenticatedUser(loginWindow.AuthenticatedUser!);
            splash.Show();
            progress.Report(StartupProgress.Create(48, "Signed in", $"Welcome {SystemSecuritySession.CurrentDisplayName} ({SystemSecuritySession.CurrentRoleName})"));

            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            await mainViewModel.InitializeFastAsync(progress);

            progress.Report(StartupProgress.Create(96, "Creating main window", "Applying theme, navigation, and the active site context"));
            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            progress.Report(StartupProgress.Create(100, "Ready", "AI WordPress Website Manager is ready"));
            var automationSettings = await _host.Services.GetRequiredService<AIWordPressManager.Application.Settings.IApplicationSettingsService>().GetAiAutomationSettingsAsync();
            var minimumSplash = TimeSpan.FromSeconds(Math.Max(3, automationSettings.MinimumSplashSeconds));
            var remainingSplash = minimumSplash - (DateTime.UtcNow - splashStartedAt);
            if (remainingSplash > TimeSpan.Zero)
                await Task.Delay(remainingSplash);

            splash.Close();
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            _ = WriteStartupCompletedLogAsync(_host.Services);
            _ = StartHostedServicesAfterUiIdleAsync();
        }
        catch (Exception exception)
        {
            progress.Report(StartupProgress.Create(100, "Startup failed", exception.Message));
            Log.Fatal(exception, "Application startup failed.");

            var crashLogPath = WriteStartupCrashLog(exception);
            var presented = false;
            try
            {
                var presenter = _host?.Services.GetService<GlobalErrorPresenter>();
                if (presenter is not null)
                {
                    presenter.Show(exception, "Startup");
                    presented = true;
                }
            }
            catch (Exception presenterException)
            {
                Log.Error(presenterException, "Could not display the startup error presenter.");
            }

            if (!presented)
            {
                try
                {
                    MessageBox.Show(
                        $"AI WordPress Manager could not start.\n\n{exception.Message}\n\nCrash log:\n{crashLogPath}",
                        "Startup error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch
                {
                }
            }

            try { splash.Close(); } catch { }
            Shutdown(-1);
        }
    }

    private static string WriteStartupCrashLog(Exception exception)
    {
        try
        {
            var directory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager",
                "Logs");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "startup-crash.log");
            var entry = string.Join(
                Environment.NewLine,
                new string('=', 80),
                DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                $"Version: {BuildIdentityDisplay.Version}",
                $"Branch: {BuildIdentityDisplay.Branch}",
                $"Commit: {BuildIdentityDisplay.Commit}",
                exception.ToString(),
                string.Empty);
            System.IO.File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIWordPressManager",
                "Logs",
                "startup-crash.log");
        }
    }

    private async Task StartHostedServicesAfterUiIdleAsync()
    {
        try
        {
            await Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            if (_host is null || _hostStarted || Dispatcher.HasShutdownStarted)
                return;

            await _host.StartAsync();
            _hostStarted = true;
            Log.Information("Hosted background services started after the desktop became idle.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Hosted background services could not start after UI initialization.");
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
            var line = $"{DateTimeOffset.Now:O} | READY | PID={Environment.ProcessId} | USER={SystemSecuritySession.CurrentUserName} | ROLE={SystemSecuritySession.CurrentRoleName} | WorkingSetMB={process.WorkingSet64 / 1024d / 1024d:N1}{Environment.NewLine}";
            await System.IO.File.AppendAllTextAsync(logPath, line);
        }
        catch
        {
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportGlobalException(args.Exception, "WPF Dispatcher", showToUser: true);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportGlobalException(args.Exception, "Background Task", showToUser: false);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                ReportGlobalException(exception, "AppDomain", showToUser: args.IsTerminating);
        };
    }

    private void ReportGlobalException(Exception exception, string source, bool showToUser)
    {
        try
        {
            Log.Error(exception, "Unhandled application exception from {Source}", source);

            var now = DateTimeOffset.UtcNow;
            var fingerprint = $"{source}|{exception.GetType().FullName}|{exception.Message}";
            if (_recentGlobalErrors.TryGetValue(fingerprint, out var previous) &&
                now - previous < GlobalErrorRepeatWindow)
            {
                return;
            }

            _recentGlobalErrors[fingerprint] = now;
            foreach (var entry in _recentGlobalErrors)
            {
                if (now - entry.Value > TimeSpan.FromMinutes(2))
                    _recentGlobalErrors.TryRemove(entry.Key, out _);
            }

            if (!showToUser || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    try
                    {
                        _host?.Services.GetService<GlobalErrorPresenter>()?.Show(exception, source);
                    }
                    catch (Exception presenterException)
                    {
                        Log.Error(presenterException, "Could not present global error from {Source}", source);
                    }
                }));
        }
        catch
        {
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        SystemSecuritySession.SignOut();
        try { ProcessTreeCleanup.KillDescendantsOfCurrentProcess(); } catch { }

        if (_host is not null)
        {
            if (_hostStarted)
            {
                try
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "Hosted services did not stop cleanly.");
                }
            }

            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.Sources.Clear();
            configuration
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "AIWM_")
                .AddCommandLine(args);
        })
        .UseSerilog((context, _, loggerConfiguration) => loggerConfiguration.ReadFrom.Configuration(context.Configuration))
        .ConfigureServices((_, services) =>
        {
            services.AddInfrastructure();
            services.AddPersistence();
            services.AddWordPress();
            services.AddAi();

            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<UiOperationService>();
            services.AddSingleton<GlobalErrorPresenter>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<AIWordPressManager.Desktop.Services.Sites.ICurrentSiteContext, AIWordPressManager.Desktop.Services.Sites.CurrentSiteContext>();
            services.AddSingleton<AIWordPressManager.Desktop.Services.Sites.ISiteOperationGuard, AIWordPressManager.Desktop.Services.Sites.SiteOperationGuard>();
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
            services.AddSingleton<AiErrorAdvisorService>();
        });
}
