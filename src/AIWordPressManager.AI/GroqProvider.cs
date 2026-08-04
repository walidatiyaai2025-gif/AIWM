namespace AIWordPressManager.AI;
public sealed class GroqProvider(IHttpClientFactory factory) : OpenAiCompatibleProviderBase(factory)
{
    public override string Name => "Groq";
    protected override string ClientName => nameof(GroqProvider);
    protected override string Endpoint => "https://api.groq.com/openai/v1/chat/completions";
}
