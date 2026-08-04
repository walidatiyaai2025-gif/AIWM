using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AIWordPressManager.Persistence.Jobs;

public sealed class ExecutionJobStore(AppDbContext dbContext) : IExecutionJobStore
{
    public async Task<Guid> StartAsync(Guid siteId, string jobType, CancellationToken cancellationToken = default)
    {
        // Job creation must never flush unrelated entities left in a long-lived
        // DbContext after a failed registration. Insert the job directly so this
        // operation is isolated from the EF change tracker.
        dbContext.ChangeTracker.Clear();

        var jobId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;
        var concurrencyToken = Guid.NewGuid().ToByteArray();

        const string sql = """
            INSERT INTO ExecutionJobs
                (Id, SiteId, JobType, Status, ProgressPercent, CurrentStep,
                 ErrorDetails, StartedAtUtc, CompletedAtUtc,
                 CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken)
            VALUES
                ($id, $siteId, $jobType, 'Running', 0, 'Starting',
                 NULL, $startedAtUtc, NULL,
                 $createdAtUtc, $updatedAtUtc, $concurrencyToken);
            """;

        var parameters = new object[]
        {
            new SqliteParameter("$id", jobId),
            new SqliteParameter("$siteId", siteId),
            new SqliteParameter("$jobType", jobType),
            new SqliteParameter("$startedAtUtc", utcNow),
            new SqliteParameter("$createdAtUtc", utcNow),
            new SqliteParameter("$updatedAtUtc", utcNow),
            new SqliteParameter("$concurrencyToken", concurrencyToken)
        };

        await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        dbContext.ChangeTracker.Clear();
        return jobId;
    }

    public async Task ReportAsync(Guid jobId, int progressPercent, string currentStep, CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        var job = await Find(jobId, cancellationToken);
        job.ReportProgress(progressPercent, currentStep, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        var job = await Find(jobId, cancellationToken);
        job.Complete(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        var job = await Find(jobId, cancellationToken);
        job.Fail(error, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        dbContext.ChangeTracker.Clear();
        var job = await Find(jobId, cancellationToken);
        job.Cancel(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionJobListItem>> GetRecentAsync(Guid? siteId = null, int take = 200, CancellationToken cancellationToken = default)
    {
        var query = dbContext.ExecutionJobs.AsNoTracking().Include(x => x.Site).AsQueryable();
        if (siteId.HasValue) query = query.Where(x => x.SiteId == siteId.Value);

        return await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(x => new ExecutionJobListItem(
                x.Id,
                x.SiteId,
                x.Site.Name,
                x.JobType,
                x.Status,
                x.ProgressPercent,
                x.CurrentStep,
                x.ErrorDetails,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ExecutionJobListItem?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        await dbContext.ExecutionJobs.AsNoTracking()
            .Where(x => x.Id == jobId)
            .Select(x => new ExecutionJobListItem(
                x.Id,
                x.SiteId,
                x.Site.Name,
                x.JobType,
                x.Status,
                x.ProgressPercent,
                x.CurrentStep,
                x.ErrorDetails,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<ExecutionJob> Find(Guid id, CancellationToken token) =>
        await dbContext.ExecutionJobs.SingleAsync(x => x.Id == id, token);
}
