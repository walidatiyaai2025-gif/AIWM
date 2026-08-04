using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Settings;
using AIWordPressManager.Domain.Enums;
using AIWordPressManager.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.Automation.Jobs;

public sealed class ScheduledWordPressSyncService(IServiceScopeFactory scopeFactory, ILogger<ScheduledWordPressSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var firstCycle = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await ReadSettingsAsync(stoppingToken);
                if (firstCycle && settings.RunOnStartup) await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
                else if (firstCycle) await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), stoppingToken);
                await SynchronizeSitesAsync(stoppingToken);
                firstCycle = false;
                settings = await ReadSettingsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled WordPress synchronization cycle failed.");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task<SynchronizationSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IApplicationSettingsService>().GetSynchronizationSettingsAsync(cancellationToken);
    }

    private async Task SynchronizeSitesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<IWordPressSynchronizationService>();
        var siteIds = await db.Sites.AsNoTracking().Where(x => !x.IsDeleted && x.ConnectionStatus == SiteConnectionStatus.Connected).Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var siteId in siteIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await sync.SynchronizeAsync(siteId, cancellationToken: cancellationToken);
            if (result.IsFailure) logger.LogWarning("Scheduled synchronization failed for {SiteId}: {Message}", siteId, result.Error.Message);
        }
    }
}
