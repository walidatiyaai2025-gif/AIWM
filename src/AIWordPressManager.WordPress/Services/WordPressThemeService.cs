using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Sites;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressThemeService(HttpClient httpClient, ISiteManagementService sites, ILogger<WordPressThemeService> logger) : IWordPressThemeService
{
    public async Task<Result<WordPressThemeDiscoveryResult>> DiscoverAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var connection = await sites.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null) return Result.Failure<WordPressThemeDiscoveryResult>(Error.NotFound("Saved WordPress credentials were not found."));
        var capabilities = new List<string>();
        try
        {
            var rootUrl = $"{connection.SiteUrl.TrimEnd('/')}/wp-json/";
            using (var rootResponse = await httpClient.GetAsync(rootUrl, cancellationToken))
            {
                var rootBody = await rootResponse.Content.ReadAsStringAsync(cancellationToken);
                if (rootResponse.IsSuccessStatusCode)
                {
                    using var root = JsonDocument.Parse(rootBody);
                    if (root.RootElement.TryGetProperty("namespaces", out var ns))
                    {
                        var values = ns.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();
                        if (values.Any(x => x.StartsWith("wc/", StringComparison.OrdinalIgnoreCase))) capabilities.Add("WooCommerce");
                        if (values.Any(x => x.Contains("elementor", StringComparison.OrdinalIgnoreCase))) capabilities.Add("Elementor REST namespace");
                        if (values.Any(x => x.Contains("rank-math", StringComparison.OrdinalIgnoreCase))) capabilities.Add("Rank Math REST namespace");
                        if (values.Any(x => x.Contains("yoast", StringComparison.OrdinalIgnoreCase))) capabilities.Add("Yoast REST namespace");
                    }
                }
            }

            using var request = CreateRequest(connection, HttpMethod.Get,
                new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/themes?status=active&context=edit&per_page=10"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                var item = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().FirstOrDefault() : default;
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var info = new WordPressThemeInfo(
                        ReadRendered(item, "name"), ReadString(item, "stylesheet"), ReadString(item, "template"),
                        ReadString(item, "version"), ReadRendered(item, "author"), ReadString(item, "status"),
                        item.TryGetProperty("is_block_theme", out var block) && block.ValueKind == JsonValueKind.True,
                        ReadString(item, "screenshot"));
                    return Result.Success(new WordPressThemeDiscoveryResult(info, capabilities, "Authenticated WordPress Themes REST endpoint",
                        "Theme metadata is read-only. Theme file editing remains disabled; future design changes will use staging, child themes, and approved CSS patches.", DateTimeOffset.Now));
                }
            }

            var homepage = await httpClient.GetStringAsync(connection.SiteUrl, cancellationToken);
            var match = Regex.Match(homepage, "/wp-content/themes/(?<slug>[^/\'\"?]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var slug = match.Groups["slug"].Value;
                var fallback = new WordPressThemeInfo(slug, slug, string.Empty, "Not exposed", "Not exposed", "active", false, string.Empty);
                return Result.Success(new WordPressThemeDiscoveryResult(fallback, capabilities, "Public homepage stylesheet detection",
                    "The authenticated themes endpoint was unavailable or permission-limited, so only the active theme folder could be detected.", DateTimeOffset.Now));
            }
            return Result.Success(new WordPressThemeDiscoveryResult(null, capabilities, "REST and homepage fallback", "The active theme could not be identified with the available permissions.", DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Theme discovery failed for site {SiteId}", siteId);
            return Result.Failure<WordPressThemeDiscoveryResult>(Error.Failure(ex.Message));
        }
    }

    private static HttpRequestMessage CreateRequest(SiteConnectionDataDto c, HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var password = new string(c.ApplicationPassword.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{password}")));
        return request;
    }
    private static string ReadString(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    private static string ReadRendered(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return string.Empty;
        if (value.ValueKind == JsonValueKind.String) return WebUtility.HtmlDecode(value.GetString() ?? string.Empty);
        return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("rendered", out var rendered) ? WebUtility.HtmlDecode(rendered.GetString() ?? string.Empty) : string.Empty;
    }
}
