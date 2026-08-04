using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Sites;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressVisualCssService(
    HttpClient httpClient,
    ISiteManagementService sites,
    IApplicationPathService paths,
    ILogger<WordPressVisualCssService> logger) : IWordPressVisualCssService
{
    public async Task<Result<BridgeDiagnosticsReport>> RunDiagnosticsAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<BridgeDiagnosticsReport>(Error.NotFound("Saved WordPress credentials were not found."));

        var checks = new List<BridgeDiagnosticCheck>();
        JsonElement? healthRoot = null;
        JsonElement? capabilityRoot = null;

        async Task<(bool Success, JsonElement? Root)> ProbeJsonAsync(string name, HttpMethod method, string relativePath)
        {
            var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}{relativePath}");
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var request = CreateRequest(connection, method, uri);
                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();
                await WriteApiLogAsync(siteId, $"Bridge diagnostics: {name}", method.Method, uri, null, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    checks.Add(new BridgeDiagnosticCheck(
                        name,
                        false,
                        $"HTTP {(int)response.StatusCode}",
                        ExtractMessage(body) ?? response.ReasonPhrase ?? "Request failed.",
                        stopwatch.ElapsedMilliseconds));
                    return (false, null);
                }

                JsonElement? root = null;
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var document = JsonDocument.Parse(body);
                    root = document.RootElement.Clone();
                }

                checks.Add(new BridgeDiagnosticCheck(
                    name,
                    true,
                    $"HTTP {(int)response.StatusCode}",
                    "Endpoint responded successfully.",
                    stopwatch.ElapsedMilliseconds));
                return (true, root);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                checks.Add(new BridgeDiagnosticCheck(
                    name,
                    false,
                    "Exception",
                    ex.Message,
                    stopwatch.ElapsedMilliseconds));
                return (false, null);
            }
        }

        var health = await ProbeJsonAsync("Authenticated health endpoint", HttpMethod.Get, "/wp-json/aiwp-manager/v1/health");
        healthRoot = health.Root;

        var capability = await ProbeJsonAsync("Visual CSS capability", HttpMethod.Get, "/wp-json/aiwp-manager/v1/visual-css");
        capabilityRoot = capability.Root;

        await ProbeJsonAsync("Visual CSS route discovery", HttpMethod.Options, "/wp-json/aiwp-manager/v1/visual-css");
        await ProbeJsonAsync("Rollback route discovery", HttpMethod.Options, "/wp-json/aiwp-manager/v1/visual-css/rollback");
        await ProbeJsonAsync("Visual CSS dry-run validation", HttpMethod.Options, "/wp-json/aiwp-manager/v1/visual-css/validate");
        await ProbeJsonAsync("Managed CSS history", HttpMethod.Options, "/wp-json/aiwp-manager/v1/visual-css/history");
        await ProbeJsonAsync("Managed CSS history rollback", HttpMethod.Options, "/wp-json/aiwp-manager/v1/visual-css/history/rollback");

        var root = healthRoot ?? capabilityRoot;
        var pluginVersion = ReadString(root, "plugin_version");
        var wordpressVersion = ReadString(root, "wordpress_version");
        var phpVersion = ReadString(root, "php_version");
        var activeTheme = ReadString(root, "active_theme");
        var activeStylesheet = ReadString(capabilityRoot ?? root, "active_stylesheet");
        var canEditPosts = ReadBool(root, "can_edit_posts");
        var canUploadFiles = ReadBool(root, "can_upload_files");
        var canEditThemeOptions = ReadBool(capabilityRoot ?? root, "can_edit_theme_options");
        var yoast = ReadNestedBool(root, "seo_plugins", "yoast");
        var rankMath = ReadNestedBool(root, "seo_plugins", "rank_math");
        var elementor = ReadNestedBool(root, "page_builders", "elementor");
        var divi = ReadNestedBool(root, "page_builders", "divi");

        checks.Add(new BridgeDiagnosticCheck(
            "Required WordPress permissions",
            canEditPosts && canEditThemeOptions,
            canEditPosts && canEditThemeOptions ? "Ready" : "Permission missing",
            $"edit_posts={canEditPosts}; edit_theme_options={canEditThemeOptions}; upload_files={canUploadFiles}",
            0));

        var versionSupported = Version.TryParse(pluginVersion, out var parsedVersion) && parsedVersion >= new Version(1, 3, 0);
        checks.Add(new BridgeDiagnosticCheck(
            "Bridge version compatibility",
            versionSupported,
            versionSupported ? "Compatible" : "Update required",
            string.IsNullOrWhiteSpace(pluginVersion)
                ? "The bridge did not report its version."
                : $"Detected {pluginVersion}; minimum supported version is 1.3.0.",
            0));

        var isReady = checks.All(check => check.Succeeded) && canEditThemeOptions && versionSupported;
        var failedCount = checks.Count(check => !check.Succeeded);
        var summary = isReady
            ? $"READY • Bridge {pluginVersion} passed all {checks.Count} diagnostics."
            : $"ATTENTION • {failedCount} of {checks.Count} diagnostics require action.";

        return Result.Success(new BridgeDiagnosticsReport(
            isReady,
            summary,
            pluginVersion,
            wordpressVersion,
            phpVersion,
            activeTheme,
            activeStylesheet,
            canEditPosts,
            canUploadFiles,
            canEditThemeOptions,
            yoast,
            rankMath,
            elementor,
            divi,
            DateTimeOffset.UtcNow,
            checks));
    }

    public async Task<Result<VisualCssCapabilityResult>> CheckCapabilityAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssCapabilityResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css");
        try
        {
            using var request = CreateRequest(connection, HttpMethod.Get, uri);
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Check Visual CSS bridge", request.Method.Method, uri, null, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssCapabilityResult>(CreateError(response, body));

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return Result.Success(new VisualCssCapabilityResult(
                BridgeAvailable: ReadBool(root, "ok"),
                CanEditThemeOptions: ReadBool(root, "can_edit_theme_options"),
                PluginVersion: ReadString(root, "plugin_version"),
                ActiveStylesheet: ReadString(root, "active_stylesheet"),
                Message: ReadString(root, "message")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Visual CSS capability check failed for {SiteId}", siteId);
            return Result.Failure<VisualCssCapabilityResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<VisualCssValidationResult>> ValidateAsync(
        Guid siteId,
        VisualCssValidationRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestModel.Selector))
            return Result.Failure<VisualCssValidationResult>(Error.Validation("A CSS selector is required."));
        if (string.IsNullOrWhiteSpace(requestModel.CssDeclarations))
            return Result.Failure<VisualCssValidationResult>(Error.Validation("CSS declarations are required."));

        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssValidationResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css/validate");
        var payload = JsonSerializer.Serialize(new
        {
            page_url = requestModel.PageUrl,
            selector = requestModel.Selector.Trim(),
            css = requestModel.CssDeclarations.Trim()
        });

        try
        {
            using var request = CreateRequest(connection, HttpMethod.Post, uri);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Validate Visual CSS dry run", request.Method.Method, uri, payload, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssValidationResult>(CreateError(response, body));

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return Result.Success(new VisualCssValidationResult(
                IsValid: ReadBool(root, "valid"),
                Message: ReadString(root, "message"),
                NormalizedSelector: ReadString(root, "selector"),
                NormalizedCss: ReadString(root, "css"),
                ActiveStylesheet: ReadString(root, "active_stylesheet"),
                ManagedCssChecksum: ReadString(root, "managed_css_checksum"),
                ManagedRuleCount: ReadInt(root, "managed_rule_count"),
                HttpStatusCode: (int)response.StatusCode,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                ResponseBody: body));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Visual CSS dry-run validation failed for {SiteId}", siteId);
            return Result.Failure<VisualCssValidationResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<VisualCssExecutionResult>> ApplyAsync(
        Guid siteId,
        VisualCssExecutionRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestModel.Selector))
            return Result.Failure<VisualCssExecutionResult>(Error.Validation("A CSS selector is required."));
        if (string.IsNullOrWhiteSpace(requestModel.CssDeclarations))
            return Result.Failure<VisualCssExecutionResult>(Error.Validation("CSS declarations are required."));

        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssExecutionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css");
        var payload = JsonSerializer.Serialize(new
        {
            page_url = requestModel.PageUrl,
            selector = requestModel.Selector.Trim(),
            css = requestModel.CssDeclarations.Trim(),
            expected_computed_style = requestModel.ExpectedComputedStyle,
            before_screenshot = requestModel.BeforeScreenshotPath
        });

        try
        {
            using var request = CreateRequest(connection, HttpMethod.Post, uri);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Apply Visual CSS", request.Method.Method, uri, payload, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssExecutionResult>(CreateError(response, body));

            return ParseExecutionResult(response, body, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Applying Visual CSS failed for {SiteId}", siteId);
            return Result.Failure<VisualCssExecutionResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<VisualCssExecutionResult>> RollbackAsync(
        Guid siteId,
        VisualCssRollbackRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssExecutionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css/rollback");
        var payload = JsonSerializer.Serialize(new
        {
            change_id = requestModel.ChangeId,
            rollback_token = requestModel.RollbackToken
        });

        try
        {
            using var request = CreateRequest(connection, HttpMethod.Post, uri);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Rollback Visual CSS", request.Method.Method, uri, payload, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssExecutionResult>(CreateError(response, body));

            return ParseExecutionResult(response, body, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rolling back Visual CSS failed for {SiteId}", siteId);
            return Result.Failure<VisualCssExecutionResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<VisualCssHistoryResult>> GetHistoryAsync(
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssHistoryResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css/history");
        try
        {
            using var request = CreateRequest(connection, HttpMethod.Get, uri);
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Load managed Visual CSS history", request.Method.Method, uri, null, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssHistoryResult>(CreateError(response, body));

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var items = new List<VisualCssHistoryItem>();
            if (root.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    items.Add(new VisualCssHistoryItem(
                        ChangeId: ReadString(item, "change_id"),
                        PageUrl: ReadString(item, "page_url"),
                        Selector: ReadString(item, "selector"),
                        CssDeclarations: ReadString(item, "css"),
                        Status: ReadString(item, "status"),
                        ActiveStylesheet: ReadString(item, "active_stylesheet"),
                        ExecutedAtUtc: ReadDate(item, "executed_at_utc") ?? DateTimeOffset.MinValue,
                        RolledBackAtUtc: ReadDate(item, "rolled_back_at_utc"),
                        ExecutedBy: ReadString(item, "executed_by")));
                }
            }

            return Result.Success(new VisualCssHistoryResult(
                PluginVersion: ReadString(root, "plugin_version"),
                ActiveStylesheet: ReadString(root, "active_stylesheet"),
                ManagedRuleCount: ReadInt(root, "managed_rule_count"),
                ManagedCssChecksum: ReadString(root, "managed_css_checksum"),
                Items: items,
                HttpStatusCode: (int)response.StatusCode,
                DurationMilliseconds: stopwatch.ElapsedMilliseconds,
                ResponseBody: body));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Loading Visual CSS history failed for {SiteId}", siteId);
            return Result.Failure<VisualCssHistoryResult>(Error.Failure(ex.Message));
        }
    }

    public async Task<Result<VisualCssExecutionResult>> RollbackHistoryAsync(
        Guid siteId,
        string changeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(changeId))
            return Result.Failure<VisualCssExecutionResult>(Error.Validation("A managed CSS change must be selected."));

        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null)
            return Result.Failure<VisualCssExecutionResult>(Error.NotFound("Saved WordPress credentials were not found."));

        var uri = new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/aiwp-manager/v1/visual-css/history/rollback");
        var payload = JsonSerializer.Serialize(new { change_id = changeId });
        try
        {
            using var request = CreateRequest(connection, HttpMethod.Post, uri);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();
            await WriteApiLogAsync(siteId, "Rollback managed Visual CSS history item", request.Method.Method, uri, payload, response, body, stopwatch.ElapsedMilliseconds, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<VisualCssExecutionResult>(CreateError(response, body));

            return ParseExecutionResult(response, body, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rolling back Visual CSS history failed for {SiteId}", siteId);
            return Result.Failure<VisualCssExecutionResult>(Error.Failure(ex.Message));
        }
    }

    private static Result<VisualCssExecutionResult> ParseExecutionResult(
        HttpResponseMessage response,
        string body,
        long durationMilliseconds)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return Result.Success(new VisualCssExecutionResult(
                Succeeded: ReadBool(root, "ok"),
                ChangeId: ReadString(root, "change_id"),
                Message: ReadString(root, "message"),
                Selector: ReadString(root, "selector"),
                CssDeclarations: ReadString(root, "css"),
                PreviousManagedCss: ReadString(root, "previous_managed_css"),
                AppliedManagedCss: ReadString(root, "applied_managed_css"),
                RollbackToken: ReadString(root, "rollback_token"),
                ExecutedAtUtc: ReadDate(root, "executed_at_utc") ?? DateTimeOffset.UtcNow,
                HttpStatusCode: (int)response.StatusCode,
                DurationMilliseconds: durationMilliseconds,
                ResponseBody: body));
        }
        catch (JsonException ex)
        {
            return Result.Failure<VisualCssExecutionResult>(Error.Failure("WordPress returned an invalid Visual CSS response: " + ex.Message));
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
        long durationMilliseconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = paths.GetLogsDirectory();
            Directory.CreateDirectory(directory);
            var record = new
            {
                utc = DateTimeOffset.UtcNow,
                siteId,
                correlationId = Guid.NewGuid().ToString("N"),
                operation,
                method,
                endpoint = endpoint.ToString(),
                requestBody = Limit(requestBody, 16000),
                statusCode = (int)response.StatusCode,
                reasonPhrase = response.ReasonPhrase,
                durationMilliseconds,
                succeeded = response.IsSuccessStatusCode,
                responseBody = Limit(responseBody, 32000),
                interpretation = Interpret(response.StatusCode)
            };
            var line = JsonSerializer.Serialize(record) + Environment.NewLine;
            await File.AppendAllTextAsync(Path.Combine(directory, "wordpress-api.log"), line, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write Visual CSS API log for {SiteId}", siteId);
        }
    }

    private static HttpRequestMessage CreateRequest(SiteConnectionDataDto connection, HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var password = new string(connection.ApplicationPassword.Where(character => !char.IsWhiteSpace(character)).ToArray());
        var bytes = Encoding.UTF8.GetBytes($"{connection.UserName}:{password}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        return request;
    }

    private static Error CreateError(HttpResponseMessage response, string body)
    {
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var property))
                message = WebUtility.HtmlDecode(property.GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
        }

        return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? Error.Unauthorized(message ?? "WordPress denied the Visual CSS operation.")
            : Error.Failure(message ?? $"WordPress returned HTTP {(int)response.StatusCode}.");
    }

    private static string Interpret(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.OK or HttpStatusCode.Created => "WordPress accepted the Visual CSS request. Reload the public page and verify the computed style before marking it complete.",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "WordPress rejected authentication or the user lacks edit_theme_options capability.",
        HttpStatusCode.NotFound => "The AI WordPress Manager Bridge endpoint was not found. Install and activate the bundled bridge plugin.",
        _ when (int)statusCode >= 500 => "WordPress or PHP returned a server error. Review the response body and PHP error log.",
        _ => "WordPress rejected the request. Review the response body before retrying."
    };

    private static string? Limit(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        return value.Length <= maximumLength ? value : value[..maximumLength] + " [truncated]";
    }


    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("message", out var property)
                ? WebUtility.HtmlDecode(property.GetString() ?? string.Empty)
                : null;
        }
        catch (JsonException)
        {
            return Limit(body, 500);
        }
    }

    private static int ReadInt(JsonElement? root, string propertyName)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object || !root.Value.TryGetProperty(propertyName, out var property))
            return 0;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : 0;
    }

    private static bool ReadBool(JsonElement? root, string propertyName) =>
        root is { } value && value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement? root, string propertyName) =>
        root is { } value && value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool ReadNestedBool(JsonElement? root, string objectName, string propertyName) =>
        root is { } value &&
        value.TryGetProperty(objectName, out var nested) &&
        nested.ValueKind == JsonValueKind.Object &&
        nested.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;


    private static DateTimeOffset? ReadDate(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;
}
