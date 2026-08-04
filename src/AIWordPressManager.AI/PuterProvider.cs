namespace AIWordPressManager.AI;

public sealed class PuterProvider(IHttpClientFactory factory) : OpenAiCompatibleProviderBase(factory)
{
    public override string Name => "Puter";
    protected override string ClientName => nameof(PuterProvider);
    protected override string Endpoint => "https://api.puter.com/puterai/openai/v1/chat/completions";
    protected override double? Temperature => null; // Puter models may reject non-default temperature values.
    protected override string MaxTokensFieldName => "max_completion_tokens";
    protected override int GetMaxTokens(int itemCount) => Math.Clamp(itemCount * 1800, 4000, 9000);
    protected override int EmptyResponseRetryCount => 1;

    protected override void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Puter authentication token is missing. Open Puter in Chrome, sign in, create an API token, then paste it in Settings.");
        base.AddHeaders(request, apiKey.Trim());
    }
}
