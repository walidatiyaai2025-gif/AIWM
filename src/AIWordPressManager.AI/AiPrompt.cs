using System.Text.Json;
using AIWordPressManager.Application.Changes;

namespace AIWordPressManager.AI;

internal static class AiPrompt
{
    public static string Build(IReadOnlyCollection<AiSuggestionInput> items) => $$"""
You are the AI recommendation engine for a WordPress management application.
Return ONLY a valid JSON array with exactly one item per input. Preserve objectId and changeType.
SetTitle: natural accurate SEO title, normally <= 60 characters.
SetSlug: concise lowercase hyphenated slug.
SetExcerpt: factual compelling excerpt/meta description, normally 120-155 characters.
Review actions: return the exact replacement text or exact safe operation, not generic advice such as “review this” or “improve SEO”. Never invent facts.
For every item, proposedValue must be directly usable by the application or a precise executable instruction with the affected value. Never propose deletion, publishing, price changes, PHP, SQL, shell commands, or bypassing approval.
RiskLevel is Low, Medium, or High. Confidence is 0..1.
Shape: [{"objectId":"...","changeType":"...","proposedValue":"...","reason":"...","confidence":0.9,"riskLevel":"Low"}]
Input: {{JsonSerializer.Serialize(items)}}
""";

    public static IReadOnlyList<AiSuggestionOutput> Parse(string text)
    {
        var cleaned = Clean(text);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        List<AiSuggestionOutput> result;
        if (cleaned.StartsWith("[", StringComparison.Ordinal))
            result = JsonSerializer.Deserialize<List<AiSuggestionOutput>>(cleaned, options) ?? [];
        else if (cleaned.StartsWith("{", StringComparison.Ordinal))
        {
            var single = JsonSerializer.Deserialize<AiSuggestionOutput>(cleaned, options);
            result = single is null ? [] : [single];
        }
        else
            throw new JsonException("The response did not contain a JSON array or object.");
        return result.Where(x => !string.IsNullOrWhiteSpace(x.ObjectId) && !string.IsNullOrWhiteSpace(x.ChangeType))
            .Select(x => x with
            {
                Confidence = Math.Clamp(x.Confidence, 0, 1),
                RiskLevel = NormalizeRisk(x.RiskLevel),
                ProposedValue = x.ProposedValue?.Trim() ?? string.Empty,
                Reason = x.Reason?.Trim() ?? string.Empty
            }).ToArray();
    }

    public static AiSuggestionInput Normalize(AiSuggestionInput input) => input with
    {
        SourceType = Limit(input.SourceType, 80), ObjectType = Limit(input.ObjectType, 80),
        ObjectId = Limit(input.ObjectId, 80), ChangeType = Limit(input.ChangeType, 80),
        CurrentValue = Limit(input.CurrentValue, 180), ProposedValue = Limit(input.ProposedValue, 180),
        Reason = Limit(input.Reason, 140), RiskLevel = NormalizeRisk(input.RiskLevel)
    };

    private static string Clean(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var first = text.IndexOf('\n');
            var last = text.LastIndexOf("```", StringComparison.Ordinal);
            if (first >= 0 && last > first) text = text[(first + 1)..last].Trim();
        }
        var arrayStart = text.IndexOf('['); var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart) return text[arrayStart..(arrayEnd + 1)];
        var objectStart = text.IndexOf('{'); var objectEnd = text.LastIndexOf('}');
        return objectStart >= 0 && objectEnd > objectStart ? text[objectStart..(objectEnd + 1)] : text;
    }

    private static string Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max] + "…";
    private static string NormalizeRisk(string? value) => value?.Equals("High", StringComparison.OrdinalIgnoreCase) == true ? "High" : value?.Equals("Medium", StringComparison.OrdinalIgnoreCase) == true ? "Medium" : "Low";
}
