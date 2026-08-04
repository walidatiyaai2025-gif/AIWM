using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressSynchronizationService(
    IWordPressExplorerService explorerService,
    IWordPressContentStore contentStore,
    IExecutionJobStore jobStore,
    IJobFailureGate jobFailureGate,
    IJobCancellationRegistry cancellationRegistry,
    ILogger<WordPressSynchronizationService> logger) : IWordPressSynchronizationService
{
    public async Task<Result<WordPressExplorerSnapshot>> SynchronizeAsync(Guid siteId, IProgress<WordPressSyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var gate = await jobFailureGate.CanStartAsync(siteId, "WordPressSync", cancellationToken);
        if (!gate.CanRun)
            return Result.Failure<WordPressExplorerSnapshot>(Error.Failure(gate.Message));

        var jobId = await jobStore.StartAsync(siteId, "WordPressSync", cancellationToken);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var registration = cancellationRegistry.Register(jobId, linkedCancellation);
        var operationToken = linkedCancellation.Token;
        try
        {
            progress?.Report(new(10, "Connecting to WordPress"));
            await jobStore.ReportAsync(jobId, 10, "Connecting to WordPress", operationToken);
            var result = await explorerService.LoadAsync(siteId, operationToken);
            if (result.IsFailure)
            {
                await jobStore.FailAsync(jobId, result.Error.Message, operationToken);
                return result;
            }

            progress?.Report(new(75, "Saving the local snapshot"));
            await jobStore.ReportAsync(jobId, 75, "Saving the local snapshot", operationToken);
            var summary = await contentStore.SaveSnapshotAsync(siteId, result.Value, operationToken);
            await jobStore.CompleteAsync(jobId, operationToken);
            progress?.Report(new(100, "Completed"));
            return Result.Success(result.Value with { SyncSummary = summary });
        }
        catch (OperationCanceledException)
        {
            await jobStore.CancelAsync(jobId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WordPress synchronization failed for site {SiteId}", siteId);
            await jobStore.FailAsync(jobId, ex.Message, CancellationToken.None);
            return Result.Failure<WordPressExplorerSnapshot>(Error.Failure(ex.Message));
        }
    }
}
