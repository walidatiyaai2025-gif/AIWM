using System.Collections.Generic;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan NavigationLoadFreshness = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, DateTimeOffset> _navigationLoadCompletedUtc =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Task> _navigationLoadsInFlight =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Coordinates the existing two-argument LoadSafelyAsync calls used by navigation.
    /// The original three-argument overload remains the actual loader and continues to
    /// be used by startup or explicit callers that provide a timeout.
    /// </summary>
    private Task LoadSafelyAsync(string module, Func<Task> loader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(loader);

        // Startup and site-wide reloads must always execute their full load path.
        if (_isInitializing || _isReloadingSiteData)
            return LoadSafelyAsync(module, loader, timeout: null);

        if (_navigationLoadsInFlight.TryGetValue(module, out var currentLoad))
            return currentLoad;

        if (_navigationLoadCompletedUtc.TryGetValue(module, out var completedUtc) &&
            DateTimeOffset.UtcNow - completedUtc < NavigationLoadFreshness)
        {
            ApplicationDataStatus = $"{module} is ready from the recent local load.";
            return Task.CompletedTask;
        }

        var coordinatedLoad = RunCoordinatedNavigationLoadAsync(module, loader);
        _navigationLoadsInFlight[module] = coordinatedLoad;
        return coordinatedLoad;
    }

    private async Task RunCoordinatedNavigationLoadAsync(string module, Func<Task> loader)
    {
        try
        {
            await LoadSafelyAsync(module, loader, timeout: null);
            _navigationLoadCompletedUtc[module] = DateTimeOffset.UtcNow;
        }
        finally
        {
            _navigationLoadsInFlight.Remove(module);
        }
    }

    /// <summary>
    /// Allows explicit refresh operations and site changes to invalidate one page or
    /// the full navigation freshness cache without cancelling an active load.
    /// </summary>
    internal void InvalidateNavigationLoadCache(string? module = null)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            _navigationLoadCompletedUtc.Clear();
            return;
        }

        _navigationLoadCompletedUtc.Remove(module);
    }
}
