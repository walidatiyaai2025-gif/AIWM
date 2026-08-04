using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions.WordPress;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressConnectionTester(
    HttpClient httpClient,
    ILogger<WordPressConnectionTester> logger) : IWordPressConnectionTester
{
    private const int MaxDiagnosticsBodyLength = 4000;

    public async Task<WordPressConnectionResult> TestAsync(
        WordPressConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Started: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        diagnostics.AppendLine($"Site URL: {request.SiteUrl}");
        diagnostics.AppendLine($"Username: {request.UserName}");
        diagnostics.AppendLine("Application Password: [REDACTED]");
        diagnostics.AppendLine();

        if (!Uri.TryCreate(request.SiteUrl.Trim(), UriKind.Absolute, out var siteUri) ||
            siteUri.Scheme is not ("http" or "https"))
        {
            diagnostics.AppendLine("Validation failed: invalid HTTP/HTTPS URL.");
            return new(false, "Enter a valid HTTP or HTTPS WordPress URL.", Diagnostics: diagnostics.ToString());
        }

        try
        {
            var rootUri = new Uri(siteUri, "/wp-json/");
            diagnostics.AppendLine($"[1] GET {rootUri}");

            using var rootRequest = new HttpRequestMessage(HttpMethod.Get, rootUri);
            using var rootResponse = await httpClient.SendAsync(rootRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var rootBody = await ReadFullBodyAsync(rootResponse, cancellationToken);
            AppendResponseDiagnostics(diagnostics, rootResponse, rootBody);

            if (!rootResponse.IsSuccessStatusCode)
            {
                var message = $"WordPress REST API returned HTTP {(int)rootResponse.StatusCode} ({rootResponse.ReasonPhrase}).";
                logger.LogWarning("WordPress root REST test failed for {SiteUrl}. Status {StatusCode}. Body: {Body}",
                    siteUri, (int)rootResponse.StatusCode, SanitizeForLog(rootBody));
                return new(false, message, Diagnostics: diagnostics.ToString());
            }

            JsonDocument rootJson;
            try
            {
                rootJson = JsonDocument.Parse(rootBody);
            }
            catch (JsonException ex)
            {
                diagnostics.AppendLine($"JSON parse error: {ex.Message}");
                logger.LogWarning(ex, "Invalid JSON from WordPress root endpoint {SiteUrl}", siteUri);
                return new(false, "The /wp-json/ response was not valid WordPress REST JSON.", Diagnostics: diagnostics.ToString());
            }

            using (rootJson)
            {
                var root = rootJson.RootElement;
                var siteName = GetString(root, "name");
                var home = GetString(root, "home");
                var namespaces = root.TryGetProperty("namespaces", out var ns) && ns.ValueKind == JsonValueKind.Array
                    ? string.Join(", ", ns.EnumerateArray().Take(20).Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                    : null;

                diagnostics.AppendLine();
                diagnostics.AppendLine($"Detected site name: {siteName ?? "(not returned)"}");
                diagnostics.AppendLine($"Detected home URL: {home ?? "(not returned)"}");
                diagnostics.AppendLine($"REST namespaces: {namespaces ?? "(not returned)"}");
                diagnostics.AppendLine();

                var userUri = new Uri(siteUri, "/wp-json/wp/v2/users/me?context=edit");
                diagnostics.AppendLine($"[2] GET {userUri}");
                diagnostics.AppendLine("Authorization: Basic [REDACTED]");

                using var userRequest = new HttpRequestMessage(HttpMethod.Get, userUri);
                var normalizedPassword = NormalizeApplicationPassword(request.ApplicationPassword);
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{request.UserName}:{normalizedPassword}"));
                userRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

                using var userResponse = await httpClient.SendAsync(userRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
                var userBody = await ReadFullBodyAsync(userResponse, cancellationToken);
                AppendResponseDiagnostics(diagnostics, userResponse, userBody);

                if (userResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var wpMessage = ExtractWordPressMessage(userBody);
                    var message = string.IsNullOrWhiteSpace(wpMessage)
                        ? "REST API was found, but WordPress rejected the username or Application Password."
                        : $"WordPress rejected the credentials: {wpMessage}";

                    logger.LogWarning("WordPress authentication rejected for {SiteUrl} and user {UserName}. Status {StatusCode}. Body: {Body}",
                        siteUri, request.UserName, (int)userResponse.StatusCode, SanitizeForLog(userBody));
                    return new(false, message, siteName, home, Diagnostics: diagnostics.ToString());
                }

                if (!userResponse.IsSuccessStatusCode)
                {
                    var wpMessage = ExtractWordPressMessage(userBody);
                    var message = string.IsNullOrWhiteSpace(wpMessage)
                        ? $"Authentication test returned HTTP {(int)userResponse.StatusCode} ({userResponse.ReasonPhrase})."
                        : $"Authentication test failed: {wpMessage}";
                    logger.LogWarning("WordPress authentication test failed for {SiteUrl}. Status {StatusCode}. Body: {Body}",
                        siteUri, (int)userResponse.StatusCode, SanitizeForLog(userBody));
                    return new(false, message, siteName, home, Diagnostics: diagnostics.ToString());
                }

                try
                {
                    using var userJson = JsonDocument.Parse(userBody);
                    int? currentUserId = userJson.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var value)
                        ? value
                        : null;
                    var userName = GetString(userJson.RootElement, "name") ?? GetString(userJson.RootElement, "slug");
                    diagnostics.AppendLine();
                    diagnostics.AppendLine($"Authenticated user ID: {currentUserId?.ToString() ?? "(not returned)"}");
                    diagnostics.AppendLine($"Authenticated user name: {userName ?? "(not returned)"}");
                    diagnostics.AppendLine("Result: SUCCESS");

                    logger.LogInformation("WordPress connection test succeeded for {SiteUrl} and user {UserName}", siteUri, request.UserName);
                    return new(true, "Connection and WordPress authentication succeeded.", siteName, home, null, null, currentUserId, diagnostics.ToString());
                }
                catch (JsonException ex)
                {
                    diagnostics.AppendLine($"Authenticated response JSON parse error: {ex.Message}");
                    logger.LogWarning(ex, "Invalid JSON from authenticated WordPress endpoint {SiteUrl}", siteUri);
                    return new(false, "The authenticated endpoint returned invalid JSON.", siteName, home, Diagnostics: diagnostics.ToString());
                }
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.AppendLine("Result: TIMEOUT after the configured HTTP timeout.");
            logger.LogWarning("WordPress connection test timed out for {SiteUrl}", request.SiteUrl);
            return new(false, "Connection timed out. Check the website URL, firewall, SSL, and security plugins.", Diagnostics: diagnostics.ToString());
        }
        catch (HttpRequestException ex)
        {
            diagnostics.AppendLine($"HTTP exception: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                diagnostics.AppendLine($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            logger.LogError(ex, "WordPress connection test failed for {SiteUrl}", request.SiteUrl);
            return new(false, $"Connection failed: {ex.Message}", Diagnostics: diagnostics.ToString());
        }
        catch (Exception ex)
        {
            diagnostics.AppendLine($"Unexpected exception: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                diagnostics.AppendLine($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            logger.LogError(ex, "Unexpected WordPress connection test error for {SiteUrl}", request.SiteUrl);
            return new(false, "An unexpected error occurred during the WordPress connection test.", Diagnostics: diagnostics.ToString());
        }
    }

    private static string NormalizeApplicationPassword(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static Task<string> ReadFullBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        response.Content.ReadAsStringAsync(cancellationToken);

    private static string CreateDiagnosticsPreview(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "(empty response)";

        return body.Length <= MaxDiagnosticsBodyLength
            ? body
            : body[..MaxDiagnosticsBodyLength] +
              $"{Environment.NewLine}[diagnostics preview truncated; full response length: {body.Length:N0} characters]";
    }

    private static void AppendResponseDiagnostics(StringBuilder diagnostics, HttpResponseMessage response, string body)
    {
        diagnostics.AppendLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
        diagnostics.AppendLine($"Content-Type: {response.Content.Headers.ContentType?.ToString() ?? "(not returned)"}");
        diagnostics.AppendLine($"Server: {string.Join("; ", response.Headers.Server.Select(x => x.ToString()))}");
        diagnostics.AppendLine("Response body:");
        diagnostics.AppendLine(CreateDiagnosticsPreview(SanitizeForDisplay(body)));
        diagnostics.AppendLine();
    }

    private static string? ExtractWordPressMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return GetString(json.RootElement, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string SanitizeForDisplay(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(empty response)" : value;

    private static string SanitizeForLog(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(empty response)" : value.Replace("\r", " ").Replace("\n", " ");

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
