using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Changes;

namespace AIWordPressManager.AI;

public abstract class OpenAiCompatibleProviderBase(IHttpClientFactory factory) : IAiProvider
{
    protected abstract string ClientName { get; }
    protected abstract string Endpoint { get; }
    public abstract string Name { get; }
    protected virtual void AddHeaders(HttpRequestMessage request, string apiKey) => request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    protected virtual double? Temperature => 0.2;
    protected virtual int GetMaxTokens(int itemCount) => Math.Clamp(itemCount * 500, 800, 2500);
    protected virtual string MaxTokensFieldName => "max_tokens";
    protected virtual int EmptyResponseRetryCount => 1;

    public async Task<IReadOnlyList<AiSuggestionOutput>> ImproveSuggestionsAsync(IReadOnlyCollection<AiSuggestionInput> suggestions, string model, string apiKey, CancellationToken cancellationToken = default)
    {
        var normalized = suggestions.Select(AiPrompt.Normalize).ToArray();
        var all = new List<AiSuggestionOutput>();
        foreach (var batch in normalized.Chunk(3))
            all.AddRange(await SendAsync(batch, model, apiKey, cancellationToken));
        return all.GroupBy(x => (x.ObjectId, x.ChangeType)).Select(x => x.First()).ToArray();
    }

    public async Task<AiProviderTestResult> TestAsync(string model, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var test = new[] { new AiSuggestionInput("Test", "Post", "test", "SetTitle", "Old title", "Better title", "Connection test", "Low") };
            var result = await SendAsync(test, model, apiKey, cancellationToken);
            return new(result.Count > 0, result.Count > 0 ? $"{Name} connection succeeded." : $"{Name} returned no data.", []);
        }
        catch (Exception ex) { return new(false, ex.Message, []); }
    }

    private async Task<IReadOnlyList<AiSuggestionOutput>> SendAsync(IReadOnlyCollection<AiSuggestionInput> batch, string model, string apiKey, CancellationToken cancellationToken)
    {
        string lastJson = string.Empty;
        string lastFinishReason = string.Empty;

        for (var attempt = 0; attempt <= EmptyResponseRetryCount; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            AddHeaders(request, apiKey);
            var tokenBudget = checked(GetMaxTokens(batch.Count) * (attempt + 1));
            var body = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["messages"] = new[] { new { role = "user", content = AiPrompt.Build(batch) } },
                [MaxTokensFieldName] = tokenBudget
            };
            if (Temperature is not null)
                body["temperature"] = Temperature.Value;

            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var client = factory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            lastJson = json;
            if (!response.IsSuccessStatusCode) throw CreateException((int)response.StatusCode, json);

            using var document = JsonDocument.Parse(json);
            var text = ExtractAssistantText(document.RootElement);
            lastFinishReason = ExtractFinishReason(document.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (attempt < EmptyResponseRetryCount && lastFinishReason.Equals("length", StringComparison.OrdinalIgnoreCase))
                    continue;

                var reason = lastFinishReason.Equals("length", StringComparison.OrdinalIgnoreCase)
                    ? "The model used the available output budget before producing visible content. Select a lighter model or try again with a shorter request."
                    : "The provider returned a response but no readable assistant text was present.";
                throw new AiProviderException(Name, (int)response.StatusCode, reason, $"{Name} returned no readable assistant text. Finish reason: {lastFinishReason}. Response: {LimitForError(json)}");
            }

            try
            {
                return AiPrompt.Parse(text);
            }
            catch (JsonException exception)
            {
                throw new AiProviderException(Name, (int)response.StatusCode, "The AI provider response was not valid structured JSON.", $"{Name} returned an unsupported response shape. {exception.Message} Raw assistant text: {LimitForError(text)}");
            }
        }

        throw new AiProviderException(Name, 200, "The AI provider did not produce a usable response.", $"Finish reason: {lastFinishReason}. Response: {LimitForError(lastJson)}");
    }

    private static string ExtractFinishReason(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                return reason.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractAssistantText(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                return ReadContent(content);
            if (choice.TryGetProperty("text", out var choiceText))
                return ReadContent(choiceText);
        }

        foreach (var propertyName in new[] { "output_text", "response", "text", "content" })
            if (root.TryGetProperty(propertyName, out var value))
            {
                var result = ReadContent(value);
                if (!string.IsNullOrWhiteSpace(result)) return result;
            }

        return string.Empty;
    }

    private static string ReadContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? string.Empty;
        if (content.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "text", "content", "value" })
                if (content.TryGetProperty(name, out var nested))
                {
                    var result = ReadContent(nested);
                    if (!string.IsNullOrWhiteSpace(result)) return result;
                }
            return string.Empty;
        }
        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                var part = ReadContent(item);
                if (!string.IsNullOrWhiteSpace(part)) parts.Add(part);
            }
            return string.Join("\n", parts);
        }
        return content.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? content.ToString() : string.Empty;
    }

    private static string LimitForError(string value) => value.Length > 900 ? value[..900] + "…" : value;

    private AiProviderException CreateException(int status, string response)
    {
        var technical = ExtractError(response);
        var friendly = status switch
        {
            401 => "The API key is invalid or unauthorized.",
            403 => "The API key does not have permission to use this model.",
            429 when technical.Contains("credit", StringComparison.OrdinalIgnoreCase) || technical.Contains("quota", StringComparison.OrdinalIgnoreCase) => "The AI provider quota or credit is exhausted.",
            429 => "The AI provider rate limit was reached. Try again later or use fallback.",
            _ when status >= 500 => "The AI provider is temporarily unavailable.",
            _ => "The AI provider rejected the request."
        };
        return new AiProviderException(Name, status, friendly, $"{Name} request failed ({status}). {technical}");
    }

    internal static string ExtractError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)) return message.GetString() ?? "Unknown error.";
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "Unknown error.";
            }
        }
        catch { }
        return json.Length > 700 ? json[..700] : json;
    }
}
