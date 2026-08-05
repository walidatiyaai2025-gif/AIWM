using System.Collections.Generic;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan NavigationLoadFreshness = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, DateTimeOffset> _navigationLoadCompletedUtc =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Task> _navigationLoadsInFlight =
        new(StringComparer.OrdinalIgnoreCase);

    private long _navigationLoadEpoch;

    /// <summary>
    /// Navigation must never wait for SQLite, WordPress, or AI data. The page is made
    /// visible by NavigateAsync first, then this overload schedules its data load at
    /// dispatcher idle priority and returns immediately. Startup and explicit callers
    /// that use the timeout overload keep their deterministic awaited behavior.
    /// </summary>
    private Task LoadSafelyAsync(string module, Func<Task> loader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentNullException.ThrowIfNull(loader);

        if (_isInitializing || _isReloadingSiteData)
            return LoadSafelyAsync(module, loader, timeout: null);

        if (_navigationLoadsInFlight.ContainsKey(module))
            return Task.CompletedTask;

        if (_navigationLoadCompletedUtc.TryGetValue(module, out var completedUtc) &&
            DateTimeOffset.UtcNow - completedUtc < NavigationLoadFreshness)
        {
            return Task.CompletedTask;
        }

        var epoch = _navigationLoadEpoch;
        var coordinatedLoad = RunBackgroundNavigationLoadAsync(module, loader, epoch);
        _navigationLoadsInFlight[module] = coordinatedLoad;
        ObserveNavigationLoad(module, coordinatedLoad);

        // NavigateAsync awaits this method. Returning a completed task lets WPF render
        // the selected page immediately while the coordinated load continues quietly.
        return Task.CompletedTask;
    }

    private async Task RunBackgroundNavigationLoadAsync(
        string module,
        Func<Task> loader,
        long scheduledEpoch)
    {
        try
        {
            var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted)
                await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            else
                await Task.Yield();

            // Do not call the UI-reporting timeout overload here. Navigation loads are
            // intentionally silent: no operation popup, progress overlay, or status rail.
            await loader();

            if (scheduledEpoch == _navigationLoadEpoch)
                _navigationLoadCompletedUtc[module] = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException)
        {
            _navigationLoadCompletedUtc.Remove(module);
        }
        catch (Exception exception)
        {
            _navigationLoadCompletedUtc.Remove(module);

            // Keep failures available for diagnostics without interrupting browsing.
            if (scheduledEpoch == _navigationLoadEpoch)
                ApplicationDataStatus = $"{module} could not load: {exception.Message}";
        }
    }

    private async void ObserveNavigationLoad(string module, Task loadTask)
    {
        try
        {
            await loadTask;
        }
        catch
        {
            // RunBackgroundNavigationLoadAsync already converts failures to a quiet
            // diagnostic status. This observer prevents unobserved task exceptions.
        }
        finally
        {
            if (_navigationLoadsInFlight.TryGetValue(module, out var current) &&
                ReferenceEquals(current, loadTask))
            {
                _navigationLoadsInFlight.Remove(module);
            }
        }
    }

    /// <summary>
    /// Explicit refreshes and site changes invalidate recent navigation data. Increasing
    /// the epoch also prevents an older site's in-flight load from becoming fresh cache.
    /// Existing loaders are allowed to finish because most current ViewModels do not yet
    /// expose cancellation tokens, but their completion cannot overwrite cache validity.
    /// </summary>
    internal void InvalidateNavigationLoadCache(string? module = null)
    {
        _navigationLoadEpoch++;

        if (string.IsNullOrWhiteSpace(module))
        {
            _navigationLoadCompletedUtc.Clear();
            return;
        }

        _navigationLoadCompletedUtc.Remove(module);
    }
}
