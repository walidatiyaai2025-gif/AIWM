using AIWordPressManager.Application.Changes;
using AIWordPressManager.Application.Settings;

namespace AIWordPressManager.Desktop.Services;

public sealed record AiErrorAdvice(string Diagnosis, string ExactAction, string RiskLevel, string Decision);

public sealed class AiErrorAdvisorService(IAiSuggestionProvider ai, IApplicationSettingsService settings)
{
    public async Task<AiErrorAdvice?> AnalyzeAsync(Exception exception, string module, CancellationToken cancellationToken = default)
    {
        var policy = await settings.GetAiAutomationSettingsAsync(cancellationToken);
        if (!policy.EnableAiErrorDiagnosis) return null;
        var input = new AiSuggestionInput("RuntimeError", "ApplicationError", module, "ResolveError",
            exception.GetType().Name, string.Empty,
            $"Analyze this application error and return one concrete user action. Message: {exception.Message}",
            ClassifyRisk(exception));
        var output = (await ai.ImproveSuggestionsAsync([input], cancellationToken)).FirstOrDefault();
        var risk = output?.RiskLevel ?? ClassifyRisk(exception);
        var action = string.IsNullOrWhiteSpace(output?.ProposedValue) ? LocalAction(exception) : output.ProposedValue;
        var diagnosis = string.IsNullOrWhiteSpace(output?.Reason) ? exception.Message : output.Reason;
        var decision = risk == "High" && policy.AutoRejectHighRiskAiActions ? "Rejected automatically by policy"
            : risk == "Low" && policy.ErrorDecisionMode == "AutoLowRisk" ? "Approved automatically by policy"
            : policy.ErrorDecisionMode == "ManualOnly" ? "Manual decision required" : "Waiting for user approval";
        return new(diagnosis, action, risk, decision);
    }

    private static string ClassifyRisk(Exception ex) => ex is UnauthorizedAccessException or System.Security.Cryptography.CryptographicException ? "High" : ex is TimeoutException or System.Net.Http.HttpRequestException ? "Medium" : "Low";
    private static string LocalAction(Exception ex) => ex switch
    {
        System.Security.Cryptography.CryptographicException => "Open Sites, re-enter the WordPress username and Application Password, then save and retry.",
        UnauthorizedAccessException => "Review the selected site's permissions and saved credential before retrying.",
        TimeoutException => "Retry once after checking the site connection; do not repeat automatically if the timeout persists.",
        System.Net.Http.HttpRequestException => "Open Sites, test the WordPress connection, verify the site URL and TLS certificate, then retry the request once.",
        _ => "Copy the correlation details, open Logs, apply the listed correction, then retry the failed operation."
    };
}
