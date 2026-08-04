using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIWordPressManager.Desktop;

internal static class SystemSecuritySession
{
    private static readonly HashSet<string> CurrentPermissions = new(StringComparer.OrdinalIgnoreCase);

    public static string CurrentUserName { get; private set; } = "Not signed in";
    public static string CurrentDisplayName { get; private set; } = "Not signed in";
    public static string CurrentRoleName { get; private set; } = "None";
    public static bool IsAdmin { get; private set; }
    public static bool IsAuthenticated { get; private set; }

    public static event EventHandler? SessionChanged;

    public static void SetAuthenticatedUser(AuthenticatedSystemUser user)
    {
        CurrentUserName = user.UserName;
        CurrentDisplayName = user.DisplayName;
        CurrentRoleName = user.RoleName;
        IsAdmin = user.IsSystemAdmin || user.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        IsAuthenticated = true;

        CurrentPermissions.Clear();
        foreach (var permission in user.Permissions)
            CurrentPermissions.Add(permission);

        SessionChanged?.Invoke(null, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    public static void SignOut()
    {
        CurrentUserName = "Not signed in";
        CurrentDisplayName = "Not signed in";
        CurrentRoleName = "None";
        IsAdmin = false;
        IsAuthenticated = false;
        CurrentPermissions.Clear();
        SessionChanged?.Invoke(null, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    public static bool HasPermission(string permissionKey)
        => IsAuthenticated && (IsAdmin || CurrentPermissions.Contains(permissionKey));

    public static string DeniedReason(string permissionKey) =>
        IsAuthenticated
            ? $"Access denied. {CurrentDisplayName} ({CurrentRoleName}) does not have permission: {permissionKey}. Contact an Admin to change the assigned role."
            : "Access denied because no authenticated system user is active.";
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
        button.SetValue(FrameworkElement.TagProperty, $"Permission:{permission}");

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
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        foreach (Window window in application.Windows)
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
