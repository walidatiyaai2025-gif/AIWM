using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Deletion;
using AIWordPressManager.Application.Settings;
using AIWordPressManager.Application.Sites;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressDeletionService(
    HttpClient httpClient,
    ISiteManagementService siteManagementService,
    IWordPressDeletionImpactStore impactStore,
    IApplicationSettingsService settingsService,
    IDatabaseBackupService databaseBackupService,
    IApplicationPathService applicationPathService,
    IExecutionJobStore jobStore,
    ILogger<WordPressDeletionService> logger) : IWordPressDeletionService
{
    public async Task<Result<WordPressDeletionResult>> MoveContentToTrashAsync(
        Guid siteId,
        string contentType,
        int wordPressId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetDestructiveOperationSettingsAsync(cancellationToken);
        if (!settings.EnableContentTrash)
            return Result.Failure<WordPressDeletionResult>(Error.Forbidden(
                "Content trash operations are disabled in Settings."));

        return await ExecuteContentDeleteAsync(
            siteId,
            contentType,
            wordPressId,
            force: false,
            operationName: "MoveContentToTrash",
            cancellationToken);
    }

    public async Task<Result<WordPressDeletionResult>> RestoreContentAsync(
        Guid siteId,
        string contentType,
        int wordPressId,
        string restoreStatus,
        CancellationToken cancellationToken = default)
    {
        var connection = await siteManagementService.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<WordPressDeletionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var endpoint = NormalizeContentEndpoint(contentType);
        if (endpoint is null)
            return Result.Failure<WordPressDeletionResult>(Error.Validation("Only posts and pages can be restored."));

        var safeStatus = restoreStatus is "publish" or "draft" or "pending" or "private" or "future"
            ? restoreStatus
            : "draft";

        var jobId = await jobStore.StartAsync(siteId, "RestoreWordPressContent", cancellationToken);
        try
        {
            using var request = CreateAuthenticatedRequest(
                connection,
                HttpMethod.Post,
                new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}/{wordPressId}"));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { status = safeStatus }),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = CreateHttpError(response, body);
                await jobStore.FailAsync(jobId, error.Message, cancellationToken);
                return Result.Failure<WordPressDeletionResult>(error);
            }

            await jobStore.CompleteAsync(jobId, cancellationToken);
            return Result.Success(new WordPressDeletionResult(
                true,
                $"{contentType} #{wordPressId} was restored with status '{safeStatus}'."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to restore WordPress content {ContentType} #{WordPressId}", contentType, wordPressId);
            await jobStore.FailAsync(jobId, ex.Message, CancellationToken.None);
            return Result.Failure<WordPressDeletionResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<WordPressDeletionResult>> DeleteContentPermanentlyAsync(
        Guid siteId,
        string contentType,
        int wordPressId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetDestructiveOperationSettingsAsync(cancellationToken);
        if (!settings.EnablePermanentContentDelete)
            return Result.Failure<WordPressDeletionResult>(Error.Forbidden(
                "Permanent content deletion is disabled in Settings."));

        return await ExecuteContentDeleteAsync(
            siteId,
            contentType,
            wordPressId,
            force: true,
            operationName: "PermanentContentDelete",
            cancellationToken);
    }

    public async Task<Result<WordPressDeletionResult>> DeleteMediaPermanentlyAsync(
        Guid siteId,
        int mediaWordPressId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetDestructiveOperationSettingsAsync(cancellationToken);
        if (!settings.EnablePermanentMediaDelete)
            return Result.Failure<WordPressDeletionResult>(Error.Forbidden(
                "Permanent media deletion is disabled in Settings."));

        var impact = await impactStore.BuildMediaPreviewAsync(siteId, mediaWordPressId, cancellationToken);
        if (impact is null)
            return Result.Failure<WordPressDeletionResult>(Error.NotFound("The local media record was not found."));

        if (impact.ReferenceCount > 0)
            return Result.Failure<WordPressDeletionResult>(Error.Conflict(
                $"Media #{mediaWordPressId} is still referenced by {impact.ReferenceCount} content item(s). Remove those references first."));

        return await DeleteMediaCoreAsync(siteId, impact, settings, cancellationToken);
    }

    public async Task<Result<WordPressDeletionResult>> DeleteContentAndExclusiveMediaAsync(
        Guid siteId,
        string contentType,
        int wordPressId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetDestructiveOperationSettingsAsync(cancellationToken);
        if (!settings.EnableContentTrash)
            return Result.Failure<WordPressDeletionResult>(Error.Forbidden(
                "Content trash operations are disabled in Settings."));
        if (!settings.EnablePermanentMediaDelete)
            return Result.Failure<WordPressDeletionResult>(Error.Forbidden(
                "Permanent media deletion is disabled in Settings."));

        var preview = await impactStore.BuildPreviewAsync(siteId, contentType, wordPressId, cancellationToken);
        if (preview is null)
            return Result.Failure<WordPressDeletionResult>(Error.NotFound("The selected content was not found in the local snapshot."));

        var trashResult = await ExecuteContentDeleteAsync(
            siteId,
            contentType,
            wordPressId,
            force: false,
            operationName: "TrashContentWithExclusiveMedia",
            cancellationToken);
        if (trashResult.IsFailure)
            return trashResult;

        var deletedMedia = new List<int>();
        var backupPaths = new List<string>();
        foreach (var media in preview.RelatedMedia.Where(x => x.SafeToDeleteWithSelectedContent))
        {
            var result = await DeleteMediaCoreAsync(siteId, media, settings, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Content was moved to trash, but media #{MediaId} could not be deleted: {Message}",
                    media.WordPressId,
                    result.Error.Message);
                continue;
            }

            deletedMedia.Add(media.WordPressId);
            if (!string.IsNullOrWhiteSpace(result.Value.BackupPath))
                backupPaths.Add(result.Value.BackupPath!);
        }

        return Result.Success(new WordPressDeletionResult(
            true,
            $"{contentType} #{wordPressId} was moved to trash. {deletedMedia.Count} exclusive media item(s) were backed up and permanently deleted. Shared media was preserved.",
            backupPaths.Count == 0 ? trashResult.Value.BackupPath : string.Join("; ", backupPaths),
            deletedMedia));
    }

    private async Task<Result<WordPressDeletionResult>> ExecuteContentDeleteAsync(
        Guid siteId,
        string contentType,
        int wordPressId,
        bool force,
        string operationName,
        CancellationToken cancellationToken)
    {
        var connection = await siteManagementService.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<WordPressDeletionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var endpoint = NormalizeContentEndpoint(contentType);
        if (endpoint is null)
            return Result.Failure<WordPressDeletionResult>(Error.Validation("Only posts and pages can be deleted."));

        var settings = await settingsService.GetDestructiveOperationSettingsAsync(cancellationToken);
        var jobId = await jobStore.StartAsync(siteId, operationName, cancellationToken);
        try
        {
            var backupPath = await BackupContentJsonAsync(connection, endpoint, wordPressId, cancellationToken);
            if (force && settings.RequireBackupBeforePermanentDelete)
                await databaseBackupService.CreateBackupAsync(cancellationToken);

            using var request = CreateAuthenticatedRequest(
                connection,
                HttpMethod.Delete,
                new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}/{wordPressId}?force={force.ToString().ToLowerInvariant()}"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = CreateHttpError(response, body);
                await jobStore.FailAsync(jobId, error.Message, cancellationToken);
                return Result.Failure<WordPressDeletionResult>(error);
            }

            await jobStore.CompleteAsync(jobId, cancellationToken);
            var verb = force ? "permanently deleted" : "moved to trash";
            return Result.Success(new WordPressDeletionResult(
                true,
                $"{contentType} #{wordPressId} was {verb}.",
                backupPath));
        }
        catch (OperationCanceledException)
        {
            await jobStore.CancelAsync(jobId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WordPress content deletion failed for {ContentType} #{WordPressId}", contentType, wordPressId);
            await jobStore.FailAsync(jobId, ex.Message, CancellationToken.None);
            return Result.Failure<WordPressDeletionResult>(Error.Failure(ex.Message));
        }
    }

    private async Task<Result<WordPressDeletionResult>> DeleteMediaCoreAsync(
        Guid siteId,
        MediaDeletionImpact impact,
        DestructiveOperationSettings settings,
        CancellationToken cancellationToken)
    {
        var connection = await siteManagementService.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<WordPressDeletionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var jobId = await jobStore.StartAsync(siteId, "PermanentMediaDelete", cancellationToken);
        try
        {
            if (settings.RequireBackupBeforePermanentDelete)
                await databaseBackupService.CreateBackupAsync(cancellationToken);

            var backupPath = await BackupMediaAsync(connection, impact, cancellationToken);
            using var request = CreateAuthenticatedRequest(
                connection,
                HttpMethod.Delete,
                new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/media/{impact.WordPressId}?force=true"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = CreateHttpError(response, body);
                await jobStore.FailAsync(jobId, error.Message, cancellationToken);
                return Result.Failure<WordPressDeletionResult>(error);
            }

            await jobStore.CompleteAsync(jobId, cancellationToken);
            return Result.Success(new WordPressDeletionResult(
                true,
                $"Media #{impact.WordPressId} was backed up and permanently deleted.",
                backupPath,
                [impact.WordPressId]));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Permanent media deletion failed for media #{MediaId}", impact.WordPressId);
            await jobStore.FailAsync(jobId, ex.Message, CancellationToken.None);
            return Result.Failure<WordPressDeletionResult>(Error.Failure(ex.Message));
        }
    }

    private async Task<string> BackupContentJsonAsync(
        SiteConnectionDataDto connection,
        string endpoint,
        int wordPressId,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedRequest(
            connection,
            HttpMethod.Get,
            new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}/{wordPressId}?context=edit"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(CreateHttpError(response, body).Message);

        var directory = CreateWordPressBackupDirectory(connection.SiteId);
        var filePath = Path.Combine(directory, $"{endpoint}_{wordPressId}.json");
        await File.WriteAllTextAsync(filePath, body, Encoding.UTF8, cancellationToken);
        return filePath;
    }

    private async Task<string> BackupMediaAsync(
        SiteConnectionDataDto connection,
        MediaDeletionImpact impact,
        CancellationToken cancellationToken)
    {
        var directory = CreateWordPressBackupDirectory(connection.SiteId);
        var metadataPath = Path.Combine(directory, $"media_{impact.WordPressId}.json");
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(impact, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(impact.SourceUrl))
            return metadataPath;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(impact.SourceUrl));
        request.Headers.UserAgent.ParseAdd("AIWordPressManager/1.0");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Media backup download failed with HTTP {(int)response.StatusCode}.");

        var extension = Path.GetExtension(new Uri(impact.SourceUrl).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
            extension = ".bin";
        var mediaPath = Path.Combine(directory, $"media_{impact.WordPressId}{extension}");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(mediaPath);
        await source.CopyToAsync(destination, cancellationToken);
        return mediaPath;
    }

    private string CreateWordPressBackupDirectory(Guid siteId)
    {
        var directory = Path.Combine(
            applicationPathService.GetBackupsDirectory(),
            "WordPress",
            siteId.ToString("N"),
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        SiteConnectionDataDto connection,
        HttpMethod method,
        Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var normalizedPassword = new string(connection.ApplicationPassword.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.UserName}:{normalizedPassword}")));
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static string? NormalizeContentEndpoint(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "post" or "posts" => "posts",
            "page" or "pages" => "pages",
            _ => null
        };

    private static Error CreateHttpError(HttpResponseMessage response, string body)
    {
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var value))
                message = WebUtility.HtmlDecode(value.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            // WordPress or a proxy may return HTML. The status code remains useful.
        }

        message = string.IsNullOrWhiteSpace(message)
            ? $"WordPress returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
            : message;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => Error.Unauthorized(message),
            HttpStatusCode.Forbidden => Error.Forbidden(message),
            HttpStatusCode.NotFound => Error.NotFound(message),
            HttpStatusCode.Conflict => Error.Conflict(message),
            _ => Error.Failure(message)
        };
    }
}
