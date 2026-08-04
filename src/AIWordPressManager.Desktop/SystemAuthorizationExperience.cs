using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIWordPressManager.Desktop;

internal static class SystemSecuritySession
{
    private static readonly HashSet<string> ManagerPermissions =
    [
        "Dashboard.View", "Sites.Manage", "WordPress.Sync", "Content.View", "Content.Edit",
        "Seo.Audit", "Suggestions.Generate", "Suggestions.Approve", "Execution.Run",
        "Execution.Rollback", "Backups.Manage", "Reports.View", "Logs.View", "Settings.Manage"
    ];

    private static readonly HashSet<string> OperatorPermissions =
    [
        "Dashboard.View", "WordPress.Sync", "Content.View", "Seo.Audit",
        "Execution.Run", "Backups.Manage", "Reports.View"
    ];

    private static readonly HashSet<string> ViewerPermissions =
    [
        "Dashboard.View", "Content.View", "Reports.View"
    ];

    public static string CurrentUserName { get; private set; } = "Admin";
    public static string CurrentRoleName { get; private set; } = "Admin";
    public static bool IsAdmin => CurrentRoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase);

    public static event EventHandler? SessionChanged;

    public static void SetCurrentUser(string userName, string roleName)
    {
        CurrentUserName = string.IsNullOrWhiteSpace(userName) ? "Unknown" : userName.Trim();
        CurrentRoleName = string.IsNullOrWhiteSpace(roleName) ? "Viewer" : roleName.Trim();
        SessionChanged?.Invoke(null, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    public static bool HasPermission(string permissionKey)
    {
        if (IsAdmin)
            return true;

        return CurrentRoleName.ToUpperInvariant() switch
        {
            "MANAGER" => ManagerPermissions.Contains(permissionKey),
            "OPERATOR" => OperatorPermissions.Contains(permissionKey),
            "VIEWER" => ViewerPermissions.Contains(permissionKey),
            _ => false
        };
    }

    public static string DeniedReason(string permissionKey) =>
        $"Access denied. {CurrentUserName} ({CurrentRoleName}) does not have permission: {permissionKey}. Contact an Admin to change the assigned role.";
}

internal static class SystemAuthorizationExperience
{
    private static readonly Dictionary<string, string> ButtonPermissionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Reset Data"] = "Database.Reset",
        ["Users & Roles"] = "Users.Manage",
        ["Add User"] = "Users.Manage",
        ["Enable / Disable"] = "Users.Manage",
        ["Execute Selected"] = "Execution.Run",
        ["Execute All Ready"] = "Execution.Run",
        ["Run Safe Plan"] = "Execution.Run",
        ["Rollback Selected"] = "Execution.Rollback",
        ["Approve Selected"] = "Suggestions.Approve",
        ["Approve All Low Risk"] = "Suggestions.Approve",
        ["Backup"] = "Backups.Manage",
        ["Restore"] = "Backups.Manage",
        ["Settings"] = "Settings.Manage"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnButtonLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            true);

        SystemSecuritySession.SessionChanged += (_, _) => RefreshOpenWindows();
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
            ApplyPermission(button);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !TryResolvePermission(button, out var permission))
            return;

        if (SystemSecuritySession.HasPermission(permission))
            return;

        e.Handled = true;
        MessageBox.Show(
            SystemSecuritySession.DeniedReason(permission),
            "Permission required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void ApplyPermission(Button button)
    {
        if (!TryResolvePermission(button, out var permission))
            return;

        var allowed = SystemSecuritySession.HasPermission(permission);
        button.IsEnabled = allowed;
        button.Tag = $"Permission:{permission}";

        if (!allowed)
            button.ToolTip = SystemSecuritySession.DeniedReason(permission);
    }

    private static bool TryResolvePermission(Button button, out string permission)
    {
        permission = string.Empty;

        if (button.Tag is string tag && tag.StartsWith("Permission:", StringComparison.OrdinalIgnoreCase))
        {
            permission = tag["Permission:".Length..].Trim();
            return !string.IsNullOrWhiteSpace(permission);
        }

        var content = button.Content switch
        {
            string text => text.Replace("\n", " ").Trim(),
            TextBlock textBlock => textBlock.Text.Trim(),
            _ => button.Content?.ToString()?.Trim() ?? string.Empty
        };

        foreach (var pair in ButtonPermissionMap)
        {
            if (content.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                permission = pair.Value;
                return true;
            }
        }

        return false;
    }

    private static void RefreshOpenWindows()
    {
        if (Application.Current is null)
            return;

        foreach (Window window in Application.Current.Windows)
        {
            foreach (var button in FindVisualChildren<Button>(window))
                ApplyPermission(button);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
                yield return typed;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}
