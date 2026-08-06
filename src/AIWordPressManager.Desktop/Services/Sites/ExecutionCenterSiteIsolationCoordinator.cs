using AIWordPressManager.Desktop.ViewModels;
using Microsoft.Extensions.Hosting;

namespace AIWordPressManager.Desktop.Services.Sites;

public sealed class ExecutionCenterSiteIsolationCoordinator(
    ICurrentSiteContext siteContext,
    ExecutionCenterViewModel executionCenter) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        siteContext.CurrentSiteChanged += OnCurrentSiteChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        siteContext.CurrentSiteChanged -= OnCurrentSiteChanged;
        return Task.CompletedTask;
    }

    private void OnCurrentSiteChanged(object? sender, CurrentSiteChangedEventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            executionCenter.HandleActiveSiteChanged(args);
            return;
        }

        _ = dispatcher.InvokeAsync(() => executionCenter.HandleActiveSiteChanged(args));
    }
}
