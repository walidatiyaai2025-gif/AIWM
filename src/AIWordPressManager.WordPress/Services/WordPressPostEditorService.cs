using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.Persistence;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Sites;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressPostEditorService(HttpClient httpClient, ISiteManagementService sites, IApplicationPathService paths, IDatabaseBackupService backups, IExecutionJobStore jobs, ILogger<WordPressPostEditorService> logger) : IWordPressPostEditorService
{
    public async Task<Result<WordPressEditableContent>> GetAsync(Guid siteId, string contentType, int wordPressId, CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null) return Result.Failure<WordPressEditableContent>(Error.NotFound("Saved WordPress credentials were not found."));
        var endpoint = NormalizeEndpoint(contentType);
        if (endpoint is null) return Result.Failure<WordPressEditableContent>(Error.Validation("Only posts and pages are supported."));
        try
        {
            var requestUri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}/{wordPressId}?context=edit");
            using var request = CreateRequest(connection, HttpMethod.Get, requestUri);
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Load editable content", request.Method.Method, requestUri, null, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);
            if (!response.IsSuccessStatusCode) return Result.Failure<WordPressEditableContent>(CreateError(response, body));
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            return Result.Success(new WordPressEditableContent(contentType, wordPressId, ReadRaw(r,"title"), ReadString(r,"slug"), ReadString(r,"status"), ReadRaw(r,"content"), ReadRaw(r,"excerpt"), ReadString(r,"link"), ReadDate(r,"date_gmt"), ReadDate(r,"modified_gmt"), ReadInt(r,"featured_media"), ReadIntArray(r,"categories"), ReadIntArray(r,"tags"), ReadString(r,"template"), ReadInt(r,"author"), ReadString(r,"comment_status"), ReadString(r,"ping_status"), ReadString(r,"format"), ReadBool(r,"sticky"), !string.IsNullOrEmpty(ReadString(r,"password")), body));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Loading editable WordPress content failed for {SiteId} {ContentType} #{Id}", siteId, contentType, wordPressId);
            return Result.Failure<WordPressEditableContent>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<WordPressContentUpdateResult>> UpdateAsync(Guid siteId, WordPressContentUpdateRequest update, CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null) return Result.Failure<WordPressContentUpdateResult>(Error.NotFound("Saved WordPress credentials were not found."));
        var endpoint = NormalizeEndpoint(update.ContentType);
        if (endpoint is null) return Result.Failure<WordPressContentUpdateResult>(Error.Validation("Only posts and pages are supported."));
        var allowedStatus = update.Status is "publish" or "future" or "draft" or "pending" or "private" ? update.Status : "draft";
        var jobId = await jobs.StartAsync(siteId, "UpdateWordPressContent", cancellationToken);
        try
        {
            var current = await GetAsync(siteId, update.ContentType, update.Id, cancellationToken);
            if (current.IsFailure) return Result.Failure<WordPressContentUpdateResult>(current.Error);
            var folder = Path.Combine(paths.GetBackupsDirectory(), "wordpress-content", DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(folder);
            var backupPath = Path.Combine(folder, $"{update.ContentType}-{update.Id}-{DateTime.Now:HHmmss}.json");
            await File.WriteAllTextAsync(backupPath, current.Value.RawJson, cancellationToken);
            await backups.CreateBackupAsync(cancellationToken);

            var payload = new Dictionary<string, object?>
            {
                ["title"] = update.Title.Trim(), ["slug"] = update.Slug.Trim(), ["status"] = allowedStatus,
                ["content"] = update.Content, ["excerpt"] = update.Excerpt, ["featured_media"] = Math.Max(0, update.FeaturedMediaId),
                ["template"] = update.Template.Trim(), ["comment_status"] = update.CommentStatus is "open" ? "open" : "closed",
                ["ping_status"] = update.PingStatus is "open" ? "open" : "closed"
            };
            if (update.ContentType.Equals("post", StringComparison.OrdinalIgnoreCase))
            {
                payload["categories"] = update.CategoryIds; payload["tags"] = update.TagIds; payload["format"] = string.IsNullOrWhiteSpace(update.Format) ? "standard" : update.Format; payload["sticky"] = update.Sticky;
            }
            if (update.DateGmt.HasValue) payload["date_gmt"] = update.DateGmt.Value.UtcDateTime.ToString("O");
            var requestUri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}/{update.Id}");
            var requestBody = JsonSerializer.Serialize(payload);
            using var request = CreateRequest(connection, HttpMethod.Post, requestUri);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Update WordPress content", request.Method.Method, requestUri, requestBody, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = CreateError(response, body); await jobs.FailAsync(jobId, error.Message, cancellationToken); return Result.Failure<WordPressContentUpdateResult>(error);
            }
            using var doc = JsonDocument.Parse(body); var r = doc.RootElement;
            await jobs.CompleteAsync(jobId, cancellationToken);
            return Result.Success(new WordPressContentUpdateResult(true, $"{update.ContentType} #{update.Id} was updated successfully.", backupPath, update.Id, ReadString(r,"status"), ReadString(r,"link"), ReadDate(r,"modified_gmt")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Updating WordPress content failed for {SiteId} {ContentType} #{Id}", siteId, update.ContentType, update.Id);
            await jobs.FailAsync(jobId, ex.Message, CancellationToken.None);
            return Result.Failure<WordPressContentUpdateResult>(Error.Failure(ex.Message));
        }
    }

    private async Task WriteApiLogAsync(
        Guid siteId,
        string operation,
        string method,
        Uri endpoint,
        string? requestBody,
        HttpResponseMessage response,
        string responseBody,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = paths.GetLogsDirectory();
            Directory.CreateDirectory(directory);
            var row = new
            {
                TimestampUtc = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString("N"),
                SiteId = siteId,
                Operation = operation,
                Method = method,
                Endpoint = endpoint.ToString(),
                RequestBody = Limit(requestBody, 12000),
                HttpStatus = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? string.Empty,
                Success = response.IsSuccessStatusCode,
                DurationMs = elapsedMilliseconds,
                ResponseBody = Limit(responseBody, 20000),
                AiInterpretation = BuildResponseInterpretation(response.StatusCode, responseBody)
            };
            var line = JsonSerializer.Serialize(row);
            await File.AppendAllTextAsync(
                Path.Combine(directory, "wordpress-api.log"),
                line + Environment.NewLine,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not write the WordPress API response log.");
        }
    }

    private static string BuildResponseInterpretation(HttpStatusCode statusCode, string body)
    {
        if ((int)statusCode is >= 200 and < 300)
            return "WordPress accepted the request. The execution pipeline must still verify the saved value by reading it again.";
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return "WordPress rejected authentication or permissions. Re-save the site Application Password and confirm the user can edit this content.";
        if ((int)statusCode == 429)
            return "WordPress or a security layer is rate-limiting requests. Pause the job and retry with backoff.";
        if ((int)statusCode >= 500)
            return "WordPress returned a server error. Review the response body, plugin logs, and PHP error log before retrying.";
        return string.IsNullOrWhiteSpace(body)
            ? "WordPress rejected the request without a response body."
            : "WordPress rejected the request. Review the returned error and route it through AI error resolution before retrying.";
    }

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return value.Length <= maximumLength ? value : value[..maximumLength] + " [truncated]";
    }

    private static string? NormalizeEndpoint(string type) => type.ToLowerInvariant() switch { "post" or "posts" => "posts", "page" or "pages" => "pages", _ => null };
    private static HttpRequestMessage CreateRequest(SiteConnectionDataDto c, HttpMethod method, Uri uri) { var request = new HttpRequestMessage(method, uri); var password = new string(c.ApplicationPassword.Where(ch=>!char.IsWhiteSpace(ch)).ToArray()); request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{password}"))); return request; }
    private static Error CreateError(HttpResponseMessage response, string body) { string? message=null; try { using var d=JsonDocument.Parse(body); if(d.RootElement.TryGetProperty("message",out var m)) message=WebUtility.HtmlDecode(m.GetString()??string.Empty); } catch(JsonException){} return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? Error.Unauthorized(message??"WordPress denied this operation.") : Error.Failure(message??$"WordPress returned HTTP {(int)response.StatusCode}."); }
    private static string ReadString(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??string.Empty:string.Empty;
    private static string ReadRaw(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.Object&&v.TryGetProperty("raw",out var raw)?raw.GetString()??string.Empty:string.Empty;
    private static int ReadInt(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&v.TryGetInt32(out var i)?i:0;
    private static bool ReadBool(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.True;
    private static DateTimeOffset? ReadDate(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&DateTimeOffset.TryParse(v.GetString(),out var d)?d:null;
    private static IReadOnlyList<int> ReadIntArray(JsonElement r,string p)=>r.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Where(x=>x.TryGetInt32(out _)).Select(x=>x.GetInt32()).ToArray():[];
}
