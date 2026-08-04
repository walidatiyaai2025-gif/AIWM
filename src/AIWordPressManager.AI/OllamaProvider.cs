using System.Text;
using System.Text.Json;
using AIWordPressManager.Application.Changes;

namespace AIWordPressManager.AI;

public sealed class OllamaProvider(IHttpClientFactory factory) : IAiProvider
{
    public string Name => "Ollama";

    public async Task<IReadOnlyList<AiSuggestionOutput>> ImproveSuggestionsAsync(
        IReadOnlyCollection<AiSuggestionInput> suggestions,
        string model,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var selectedModel = string.IsNullOrWhiteSpace(model) ? "qwen3:4b" : model.Trim();
        var normalized = suggestions.Select(AiPrompt.Normalize).ToArray();
        var all = new List<AiSuggestionOutput>();

        foreach (var batch in normalized.Chunk(2))
            all.AddRange(await SendAsync(batch, selectedModel, cancellationToken));

        return all.GroupBy(x => (x.ObjectId, x.ChangeType)).Select(x => x.First()).ToArray();
    }

    public async Task<AiProviderTestResult> TestAsync(string model, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = factory.CreateClient(nameof(OllamaProvider));
            using var response = await client.GetAsync("api/tags", cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, $"Ollama returned HTTP {(int)response.StatusCode}.", []);

            using var document = JsonDocument.Parse(json);
            var models = document.RootElement.TryGetProperty("models", out var array)
                ? array.EnumerateArray()
                    .Select(x => x.TryGetProperty("name", out var name) ? name.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToArray()
                : [];

            if (models.Length == 0)
                return new(false, "Ollama is running, but no local model is installed. Run: ollama pull qwen3:4b", []);

            return new(true, "Ollama local connection succeeded. No API key or paid credit is required.", models);
        }
        catch (HttpRequestException)
        {
            return new(false, "Ollama is not reachable at http://localhost:11434. Install and start Ollama first.", []);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message, []);
        }
    }

    private async Task<IReadOnlyList<AiSuggestionOutput>> SendAsync(
        IReadOnlyCollection<AiSuggestionInput> batch,
        string model,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            model,
            messages = new[] { new { role = "user", content = AiPrompt.Build(batch) } },
            stream = false,
            format = "json",
            options = new { temperature = 0.2, num_predict = Math.Clamp(batch.Count * 550, 800, 1800) }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        var client = factory.CreateClient(nameof(OllamaProvider));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AiProviderException("Ollama", (int)response.StatusCode, "The local Ollama model rejected the request.", $"Ollama request failed ({(int)response.StatusCode}). {json}");

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return AiPrompt.Parse(text);
    }
}
