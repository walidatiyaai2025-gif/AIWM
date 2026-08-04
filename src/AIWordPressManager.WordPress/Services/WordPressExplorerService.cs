using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Common.Results;
using AIWordPressManager.Application.Sites;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.WordPress.Services;

public sealed class WordPressExplorerService(HttpClient httpClient, ISiteManagementService siteManagementService, ILogger<WordPressExplorerService> logger) : IWordPressExplorerService
{
    private const int PageSize = 50;
    private const int MaximumPagesPerCollection = 10;

    public async Task<Result<WordPressExplorerSnapshot>> LoadAsync(Guid siteId, CancellationToken cancellationToken = default)
    {
        var connection = await siteManagementService.GetConnectionDataAsync(siteId, cancellationToken);
        if (connection is null) return Result.Failure<WordPressExplorerSnapshot>(Error.NotFound("No saved WordPress credentials were found for the selected site."));
        try
        {
            var posts = await GetContentAsync(connection, "posts", cancellationToken); if (posts.IsFailure) return Result.Failure<WordPressExplorerSnapshot>(posts.Error);
            var pages = await GetContentAsync(connection, "pages", cancellationToken); if (pages.IsFailure) return Result.Failure<WordPressExplorerSnapshot>(pages.Error);
            var categories = await GetTermsAsync(connection, "categories", cancellationToken); if (categories.IsFailure) return Result.Failure<WordPressExplorerSnapshot>(categories.Error);
            var tags = await GetTermsAsync(connection, "tags", cancellationToken); if (tags.IsFailure) return Result.Failure<WordPressExplorerSnapshot>(tags.Error);
            var media = await GetMediaAsync(connection, cancellationToken); if (media.IsFailure) return Result.Failure<WordPressExplorerSnapshot>(media.Error);
            return Result.Success(new WordPressExplorerSnapshot(posts.Value.Items, pages.Value.Items,
                categories.Value.Items.Select(x => new WordPressCategoryItem(x.Id,x.Name,x.Slug,x.Count)).ToArray(),
                tags.Value.Items.Select(x => new WordPressTagItem(x.Id,x.Name,x.Slug,x.Count)).ToArray(), media.Value.Items,
                posts.Value.Total,pages.Value.Total,categories.Value.Total,tags.Value.Total,media.Value.Total,DateTimeOffset.Now,WordPressSyncSummary.Empty));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Result.Failure<WordPressExplorerSnapshot>(Error.Failure("WordPress synchronization was cancelled.")); }
        catch (TaskCanceledException) { return Result.Failure<WordPressExplorerSnapshot>(Error.Failure("WordPress Explorer timed out while reading the site.")); }
        catch (HttpRequestException ex) { logger.LogError(ex,"WordPress Explorer request failed for site {SiteId}",siteId); return Result.Failure<WordPressExplorerSnapshot>(Error.Failure($"WordPress request failed: {ex.Message}")); }
        catch (JsonException ex) { logger.LogError(ex,"WordPress Explorer returned invalid JSON for site {SiteId}",siteId); return Result.Failure<WordPressExplorerSnapshot>(Error.Failure("WordPress returned invalid JSON while loading Explorer data.")); }
    }

    private async Task<Result<PagedResult<WordPressContentItem>>> GetContentAsync(SiteConnectionDataDto connection,string type,CancellationToken ct)
    {
        var all=new List<WordPressContentItem>(); var total=0; var totalPages=1;
        for(var page=1; page<=Math.Min(totalPages,MaximumPagesPerCollection); page++)
        {
            var uri=new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{type}?context=edit&status=publish,future,draft,pending,private,trash&per_page={PageSize}&page={page}&orderby=modified&order=desc&_fields=id,title,slug,status,link,modified_gmt,content,excerpt");
            using var response=await SendAsync(connection,uri,ct); var body=await response.Content.ReadAsStringAsync(ct); if(!response.IsSuccessStatusCode) return Result.Failure<PagedResult<WordPressContentItem>>(CreateHttpError(response,body));
            total=ReadHeaderInt(response,"X-WP-Total",total); totalPages=ReadHeaderInt(response,"X-WP-TotalPages",1);
            using var document=JsonDocument.Parse(body); all.AddRange(document.RootElement.EnumerateArray().Select(item=>new WordPressContentItem(item.GetProperty("id").GetInt32(),ReadRenderedTitle(item),ReadString(item,"slug"),ReadString(item,"status"),ReadString(item,"link"),ReadDate(item,"modified_gmt"),ReadRenderedObject(item,"content"),ReadRenderedObject(item,"excerpt"))));
        }
        return Result.Success(new PagedResult<WordPressContentItem>(all,total));
    }

    private async Task<Result<PagedResult<TermItem>>> GetTermsAsync(SiteConnectionDataDto connection,string endpoint,CancellationToken ct)
    {
        var all=new List<TermItem>(); var total=0; var totalPages=1;
        for(var page=1; page<=Math.Min(totalPages,MaximumPagesPerCollection); page++)
        {
            var uri=new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/{endpoint}?context=edit&per_page={PageSize}&page={page}&orderby=count&order=desc&_fields=id,name,slug,count");
            using var response=await SendAsync(connection,uri,ct); var body=await response.Content.ReadAsStringAsync(ct); if(!response.IsSuccessStatusCode) return Result.Failure<PagedResult<TermItem>>(CreateHttpError(response,body));
            total=ReadHeaderInt(response,"X-WP-Total",total); totalPages=ReadHeaderInt(response,"X-WP-TotalPages",1);
            using var doc=JsonDocument.Parse(body); all.AddRange(doc.RootElement.EnumerateArray().Select(x=>new TermItem(x.GetProperty("id").GetInt32(),WebUtility.HtmlDecode(ReadString(x,"name")),ReadString(x,"slug"),x.TryGetProperty("count",out var c)?c.GetInt32():0)));
        }
        return Result.Success(new PagedResult<TermItem>(all,total));
    }

    private async Task<Result<PagedResult<WordPressMediaItem>>> GetMediaAsync(SiteConnectionDataDto connection,CancellationToken ct)
    {
        var all=new List<WordPressMediaItem>(); var total=0; var totalPages=1;
        for(var page=1; page<=Math.Min(totalPages,MaximumPagesPerCollection); page++)
        {
            var uri=new Uri($"{connection.SiteUrl.TrimEnd('/')}/wp-json/wp/v2/media?context=edit&per_page={PageSize}&page={page}&orderby=modified&order=desc&_fields=id,title,slug,media_type,mime_type,source_url,modified_gmt");
            using var response=await SendAsync(connection,uri,ct); var body=await response.Content.ReadAsStringAsync(ct); if(!response.IsSuccessStatusCode) return Result.Failure<PagedResult<WordPressMediaItem>>(CreateHttpError(response,body));
            total=ReadHeaderInt(response,"X-WP-Total",total); totalPages=ReadHeaderInt(response,"X-WP-TotalPages",1);
            using var doc=JsonDocument.Parse(body); all.AddRange(doc.RootElement.EnumerateArray().Select(x=>new WordPressMediaItem(x.GetProperty("id").GetInt32(),ReadRenderedTitle(x),ReadString(x,"slug"),ReadString(x,"media_type"),ReadString(x,"mime_type"),ReadString(x,"source_url"),ReadDate(x,"modified_gmt"))));
        }
        return Result.Success(new PagedResult<WordPressMediaItem>(all,total));
    }

    private async Task<HttpResponseMessage> SendAsync(SiteConnectionDataDto c,Uri uri,CancellationToken ct) { using var request=new HttpRequestMessage(HttpMethod.Get,uri); var pwd=new string(c.ApplicationPassword.Where(ch=>!char.IsWhiteSpace(ch)).ToArray()); request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{pwd}"))); return await httpClient.SendAsync(request,HttpCompletionOption.ResponseContentRead,ct); }
    private static int ReadHeaderInt(HttpResponseMessage r,string n,int f)=>r.Headers.TryGetValues(n,out var v)&&int.TryParse(v.FirstOrDefault(),out var p)?p:f;
    private static Error CreateHttpError(HttpResponseMessage r,string body) { string? m=null; try { using var d=JsonDocument.Parse(body); if(d.RootElement.TryGetProperty("message",out var v)) m=v.GetString(); } catch(JsonException){} m??=$"WordPress returned HTTP {(int)r.StatusCode} ({r.ReasonPhrase})."; return r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden?Error.Unauthorized(m):Error.Failure(m); }
    private static string ReadRenderedTitle(JsonElement i)=>i.TryGetProperty("title",out var t)&&t.ValueKind==JsonValueKind.Object&&t.TryGetProperty("rendered",out var r)?WebUtility.HtmlDecode(r.GetString()??"(Untitled)"):"(Untitled)";
    private static string ReadRenderedObject(JsonElement i,string p)=>i.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.Object&&v.TryGetProperty("rendered",out var r)?r.GetString()??string.Empty:string.Empty;
    private static string ReadString(JsonElement i,string p)=>i.TryGetProperty(p,out var v)?v.GetString()??string.Empty:string.Empty;
    private static DateTimeOffset? ReadDate(JsonElement i,string p)=>i.TryGetProperty(p,out var v)&&DateTimeOffset.TryParse(v.GetString(),out var d)?d:null;
    private sealed record PagedResult<T>(IReadOnlyList<T> Items,int Total);
    private sealed record TermItem(int Id,string Name,string Slug,int Count);
}
