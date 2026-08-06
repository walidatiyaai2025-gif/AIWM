using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Central runtime governance for DispatcherTimer instances owned by windows and view models.
/// Timers belonging to hidden/closed workspaces are stopped, preventing stale refreshes and
/// delayed popup activity. A readable audit is persisted for diagnostics.
/// </summary>
internal static class CentralTimerGovernanceExperience
{
    private static readonly ConditionalWeakTable<Window, WindowTimerState> States = new();
    private static readonly object AuditGate = new();
    private static readonly string AuditPath = BuildAuditPath();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnWindowUnloaded),
            true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        if (!States.TryGetValue(window, out var state))
        {
            state = new WindowTimerState(window);
            States.Add(window, state);
        }

        state.RefreshInventory();
        state.ApplyVisibilityPolicy();

        window.IsVisibleChanged -= OnWindowVisibilityChanged;
        window.IsVisibleChanged += OnWindowVisibilityChanged;
        window.Activated -= OnWindowActivated;
        window.Activated += OnWindowActivated;
        window.Deactivated -= OnWindowDeactivated;
        window.Deactivated += OnWindowDeactivated;
        window.Closed -= OnWindowClosed;
        window.Closed += OnWindowClosed;
    }

    private static void OnWindowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window && States.TryGetValue(window, out var state))
            state.StopWorkspaceTimers("window unloaded");
    }

    private static void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Window window && States.TryGetValue(window, out var state))
        {
            state.RefreshInventory();
            state.ApplyVisibilityPolicy();
        }
    }

    private static void OnWindowActivated(object? sender, EventArgs e)
    {
        if (sender is Window window && States.TryGetValue(window, out var state))
        {
            state.RefreshInventory();
            state.RestoreEligibleTimers();
        }
    }

    private static void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (sender is Window window && States.TryGetValue(window, out var state) && window is not MainWindow)
            state.StopPopupTimers("window deactivated");
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        if (States.TryGetValue(window, out var state))
            state.StopWorkspaceTimers("window closed");

        window.IsVisibleChanged -= OnWindowVisibilityChanged;
        window.Activated -= OnWindowActivated;
        window.Deactivated -= OnWindowDeactivated;
        window.Closed -= OnWindowClosed;
        States.Remove(window);
    }

    private sealed class WindowTimerState
    {
        private static readonly string[] PopupTokens =
        [
            "popup", "dialog", "loading", "progress", "journey", "review", "notification", "toast"
        ];

        private readonly Window _window;
        private readonly Dictionary<DispatcherTimer, TimerRegistration> _timers = new(ReferenceEqualityComparer<DispatcherTimer>.Instance);

        internal WindowTimerState(Window window) => _window = window;

        internal void RefreshInventory()
        {
            foreach (var candidate in EnumerateOwners(_window))
                DiscoverTimers(candidate);

            WriteAudit(_window, _timers.Values);
        }

        internal void ApplyVisibilityPolicy()
        {
            if (!_window.IsVisible || !_window.IsLoaded)
                StopWorkspaceTimers("workspace hidden");
            else if (_window.IsActive || _window is MainWindow)
                RestoreEligibleTimers();
        }

        internal void StopWorkspaceTimers(string reason)
        {
            foreach (var registration in _timers.Values)
                Stop(registration, reason);
            WriteAudit(_window, _timers.Values);
        }

        internal void StopPopupTimers(string reason)
        {
            foreach (var registration in _timers.Values.Where(x => x.IsPopupRelated))
                Stop(registration, reason);
            WriteAudit(_window, _timers.Values);
        }

        internal void RestoreEligibleTimers()
        {
            if (!_window.IsVisible || !_window.IsLoaded)
                return;

            foreach (var registration in _timers.Values)
            {
                if (!registration.WasRunningBeforeGovernance || registration.Timer.IsEnabled)
                    continue;

                // Popup-related timers are never restarted merely because a window becomes active.
                // They must be started by the owning user action.
                if (registration.IsPopupRelated)
                    continue;

                registration.Timer.Start();
                registration.LastAction = "restored for active workspace";
                registration.LastActionUtc = DateTime.UtcNow;
            }
            WriteAudit(_window, _timers.Values);
        }

        private void DiscoverTimers(object owner)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in owner.GetType().GetFields(flags))
            {
                object? value;
                try { value = field.GetValue(field.IsStatic ? null : owner); }
                catch { continue; }

                if (value is DispatcherTimer timer)
                    Register(timer, owner.GetType().Name, field.Name);
            }

            foreach (var property in owner.GetType().GetProperties(flags))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || property.PropertyType != typeof(DispatcherTimer))
                    continue;

                object? value;
                try { value = property.GetValue(property.GetMethod?.IsStatic == true ? null : owner); }
                catch { continue; }

                if (value is DispatcherTimer timer)
                    Register(timer, owner.GetType().Name, property.Name);
            }
        }

        private void Register(DispatcherTimer timer, string ownerName, string memberName)
        {
            if (_timers.ContainsKey(timer))
                return;

            var identity = $"{ownerName}.{memberName}";
            var popupRelated = PopupTokens.Any(token => identity.Contains(token, StringComparison.OrdinalIgnoreCase));
            _timers.Add(timer, new TimerRegistration(
                timer,
                ownerName,
                memberName,
                timer.IsEnabled,
                popupRelated,
                "discovered",
                DateTime.UtcNow));
        }

        private static void Stop(TimerRegistration registration, string reason)
        {
            if (registration.Timer.IsEnabled)
            {
                registration.WasRunningBeforeGovernance = true;
                registration.Timer.Stop();
            }
            registration.LastAction = reason;
            registration.LastActionUtc = DateTime.UtcNow;
        }

        private static IEnumerable<object> EnumerateOwners(Window window)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer<object>.Instance);
            var queue = new Queue<(object Value, int Depth)>();
            queue.Enqueue((window, 0));
            if (window.DataContext is not null)
                queue.Enqueue((window.DataContext, 0));

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                if (!visited.Add(current))
                    continue;

                yield return current;
                if (depth >= 2)
                    continue;

                foreach (var property in current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string))
                        continue;

                    object? value;
                    try { value = property.GetValue(current); }
                    catch { continue; }

                    if (value is null || value.GetType().IsPrimitive || value is Delegate || value is DispatcherObject)
                        continue;

                    if (value is IEnumerable enumerable)
                    {
                        var count = 0;
                        foreach (var item in enumerable)
                        {
                            if (item is not null && count++ < 20)
                                queue.Enqueue((item, depth + 1));
                        }
                    }
                    else
                    {
                        queue.Enqueue((value, depth + 1));
                    }
                }
            }
        }
    }

    private sealed class TimerRegistration
    {
        internal TimerRegistration(
            DispatcherTimer timer,
            string ownerName,
            string memberName,
            bool wasRunningBeforeGovernance,
            bool isPopupRelated,
            string lastAction,
            DateTime lastActionUtc)
        {
            Timer = timer;
            OwnerName = ownerName;
            MemberName = memberName;
            WasRunningBeforeGovernance = wasRunningBeforeGovernance;
            IsPopupRelated = isPopupRelated;
            LastAction = lastAction;
            LastActionUtc = lastActionUtc;
        }

        internal DispatcherTimer Timer { get; }
        internal string OwnerName { get; }
        internal string MemberName { get; }
        internal bool WasRunningBeforeGovernance { get; set; }
        internal bool IsPopupRelated { get; }
        internal string LastAction { get; set; }
        internal DateTime LastActionUtc { get; set; }
    }

    private static void WriteAudit(Window window, IEnumerable<TimerRegistration> registrations)
    {
        try
        {
            lock (AuditGate)
            {
                var rows = registrations
                    .OrderBy(x => x.OwnerName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var builder = new StringBuilder();
                builder.AppendLine("AI WordPress Manager - Runtime Timer Audit");
                builder.AppendLine($"Updated UTC: {DateTime.UtcNow:O}");
                builder.AppendLine($"Window: {window.GetType().Name} | Title: {window.Title} | Visible: {window.IsVisible} | Active: {window.IsActive}");
                builder.AppendLine(new string('-', 120));
                builder.AppendLine("Owner | Member | Interval | Enabled | PopupRelated | LastAction | LastActionUtc");

                foreach (var row in rows)
                {
                    builder.AppendLine(
                        $"{row.OwnerName} | {row.MemberName} | {row.Timer.Interval} | {row.Timer.IsEnabled} | " +
                        $"{row.IsPopupRelated} | {row.LastAction} | {row.LastActionUtc:O}");
                }

                builder.AppendLine();
                File.AppendAllText(AuditPath, builder.ToString());
            }
        }
        catch
        {
            // Diagnostics must never interrupt the application.
        }
    }

    private static string BuildAuditPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager",
            "Diagnostics");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "runtime-timers.log");
    }

    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static ReferenceEqualityComparer<T> Instance { get; } = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
