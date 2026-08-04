namespace AIWordPressManager.AI;
public sealed class OpenAiProvider(IHttpClientFactory factory) : OpenAiCompatibleProviderBase(factory)
{
    public override string Name => "OpenAI";
    protected override string ClientName => nameof(OpenAiProvider);
    protected override string Endpoint => "https://api.openai.com/v1/chat/completions";
}
