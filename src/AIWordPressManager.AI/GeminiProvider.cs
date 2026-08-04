using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Changes;

namespace AIWordPressManager.AI;

public sealed class GeminiProvider(IHttpClientFactory factory) : IAiProvider
{
    public string Name => "Gemini";

    public async Task<IReadOnlyList<AiSuggestionOutput>> ImproveSuggestionsAsync(
        IReadOnlyCollection<AiSuggestionInput> suggestions,
        string model,
        string apiKey,
        CancellationToken cancellationToken = default)
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
            var models = await LoadModelsAsync(apiKey, cancellationToken);
            return new(result.Count > 0, result.Count > 0 ? "Gemini connection succeeded." : "Gemini returned no data.", models);
        }
        catch (Exception ex) { return new(false, ex.Message, []); }
    }

    private async Task<IReadOnlyList<string>> LoadModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        var client = factory.CreateClient(nameof(GeminiProvider));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("models", out var models)) return [];
        return models.EnumerateArray()
            .Where(x => x.TryGetProperty("supportedGenerationMethods", out var methods) && methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
            .Select(x => x.GetProperty("name").GetString()?.Replace("models/", string.Empty))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x).ToArray();
    }

    private async Task<IReadOnlyList<AiSuggestionOutput>> SendAsync(IReadOnlyCollection<AiSuggestionInput> batch, string model, string apiKey, CancellationToken cancellationToken)
    {
        var cleanModel = model.Replace("models/", string.Empty, StringComparison.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(cleanModel)}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        var body = new
        {
            contents = new[] { new { parts = new[] { new { text = AiPrompt.Build(batch) } } } },
            generationConfig = new { temperature = 0.2, responseMimeType = "application/json", maxOutputTokens = Math.Clamp(batch.Count * 500, 800, 2500) }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var client = factory.CreateClient(nameof(GeminiProvider));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw CreateException((int)response.StatusCode, json);
        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;
        return AiPrompt.Parse(text);
    }

    private static AiProviderException CreateException(int status, string response)
    {
        var technical = OpenAiCompatibleProviderBase.ExtractError(response);
        var friendly = status switch
        {
            400 => "Gemini rejected the request or the selected model name is invalid.",
            401 or 403 => "The Gemini API key is invalid, restricted, or unauthorized.",
            429 => "The Gemini free quota or rate limit was reached.",
            _ when status >= 500 => "Gemini is temporarily unavailable.",
            _ => "Gemini rejected the request."
        };
        return new AiProviderException("Gemini", status, friendly, $"Gemini request failed ({status}). {technical}");
    }
}
