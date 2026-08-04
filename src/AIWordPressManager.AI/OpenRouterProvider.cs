namespace AIWordPressManager.AI;
public sealed class OpenRouterProvider(IHttpClientFactory factory) : OpenAiCompatibleProviderBase(factory)
{
    public override string Name => "OpenRouter";
    protected override string ClientName => nameof(OpenRouterProvider);
    protected override string Endpoint => "https://openrouter.ai/api/v1/chat/completions";
    protected override void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        base.AddHeaders(request, apiKey);
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://localhost/AIWordPressManager");
        request.Headers.TryAddWithoutValidation("X-Title", "AI WordPress Manager");
    }
}
