using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Settings;
using AIWordPressManager.Domain.Enums;
using AIWordPressManager.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.Automation.Jobs;

public sealed class ScheduledWordPressSyncService(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledWordPressSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupQuietPeriod = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan BetweenSitesDelay = TimeSpan.FromMilliseconds(750);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var firstCycle = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await ReadSettingsAsync(stoppingToken);
                var delay = firstCycle && settings.RunOnStartup
                    ? StartupQuietPeriod
                    : TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));

                await Task.Delay(delay, stoppingToken);
                stoppingToken.ThrowIfCancellationRequested();

                // Settings may have changed while the service was waiting.
                settings = await ReadSettingsAsync(stoppingToken);
                await SynchronizeSitesAsync(stoppingToken);

                firstCycle = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled WordPress synchronization cycle failed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                firstCycle = false;
            }
        }
    }

    private async Task<SynchronizationSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IApplicationSettingsService>()
            .GetSynchronizationSettingsAsync(cancellationToken);
    }

    private async Task SynchronizeSitesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<IWordPressSynchronizationService>();

        var siteIds = await db.Sites
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.ConnectionStatus == SiteConnectionStatus.Connected)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < siteIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var siteId = siteIds[index];
            var result = await sync.SynchronizeAsync(siteId, cancellationToken: cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Scheduled synchronization failed for {SiteId}: {Message}",
                    siteId,
                    result.Error.Message);
            }

            if (index < siteIds.Count - 1)
                await Task.Delay(BetweenSitesDelay, cancellationToken);
        }
    }
}
