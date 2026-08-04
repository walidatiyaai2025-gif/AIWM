using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.WordPress.Services;
using AIWordPressManager.Application.Deletion;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.WordPress;

public static class DependencyInjection
{
    public static IServiceCollection AddWordPress(this IServiceCollection services)
    {
        services.AddHttpClient<IWordPressConnectionTester, WordPressConnectionTester>(ConfigureClient);
        services.AddHttpClient<IWordPressExplorerService, WordPressExplorerService>(ConfigureClient);
        services.AddHttpClient<IWordPressDeletionService, WordPressDeletionService>(ConfigureClient);
        services.AddHttpClient<IWordPressThemeService, WordPressThemeService>(ConfigureClient);
        services.AddHttpClient<IWordPressPostEditorService, WordPressPostEditorService>(ConfigureClient);
        services.AddHttpClient<IWordPressVisualCssService, WordPressVisualCssService>(ConfigureClient);
        services.AddScoped<IWordPressSynchronizationService, WordPressSynchronizationService>();
        return services;
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AIWordPressManager/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }
}
