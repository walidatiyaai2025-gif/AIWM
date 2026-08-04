using AIWordPressManager.Application.Changes;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(PuterProvider), c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddHttpClient(nameof(OllamaProvider), c =>
        {
            c.BaseAddress = new Uri("http://localhost:11434/");
            c.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient(nameof(GeminiProvider), c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddHttpClient(nameof(GroqProvider), c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddHttpClient(nameof(OpenRouterProvider), c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddHttpClient(nameof(OpenAiProvider), c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddScoped<IAiProvider, PuterProvider>();
        services.AddScoped<IAiProvider, OllamaProvider>();
        services.AddScoped<IAiProvider, GeminiProvider>();
        services.AddScoped<IAiProvider, GroqProvider>();
        services.AddScoped<IAiProvider, OpenRouterProvider>();
        services.AddScoped<IAiProvider, OpenAiProvider>();
        services.AddScoped<IAiSuggestionProvider, MultiProviderSuggestionProvider>();
        return services;
    }
}
