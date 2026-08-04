using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal sealed record AuthenticatedSystemUser(
    string UserName,
    string DisplayName,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    bool IsSystemAdmin);

internal static class SystemAuthenticationService
{
    public static async Task<AuthenticatedSystemUser?> AuthenticateAsync(
        string databasePath,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
            return null;

        await UserSecurityStore.EnsureCreatedAsync(databasePath);
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.UserName, u.DisplayName, u.PasswordHash, u.PasswordSalt,
                   u.IsActive, u.IsSystemAdmin, COALESCE(r.Name, 'Viewer')
            FROM SystemUsers u
            LEFT JOIN SystemUserRoles ur ON ur.UserId = u.Id
            LEFT JOIN SystemRoles r ON r.Id = ur.RoleId
            WHERE UPPER(u.UserName) = UPPER($user)
            ORDER BY CASE WHEN r.Name = 'Admin' THEN 0 ELSE 1 END
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$user", userName.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetInt64(4) != 1)
            return null;

        var storedHash = Convert.FromBase64String(reader.GetString(2));
        var salt = Convert.FromBase64String(reader.GetString(3));
        var suppliedHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            210_000,
            HashAlgorithmName.SHA256,
            32);

        if (!CryptographicOperations.FixedTimeEquals(storedHash, suppliedHash))
            return null;

        var resolvedUserName = reader.GetString(0);
        var displayName = reader.GetString(1);
        var isAdmin = reader.GetInt64(5) == 1;
        var roleName = reader.GetString(6);
        await reader.DisposeAsync();

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var permissionsCommand = connection.CreateCommand())
        {
            permissionsCommand.CommandText = """
                SELECT DISTINCT p.PermissionKey
                FROM SystemUsers u
                JOIN SystemUserRoles ur ON ur.UserId = u.Id
                JOIN SystemRolePermissions rp ON rp.RoleId = ur.RoleId
                JOIN SystemPermissions p ON p.Id = rp.PermissionId
                WHERE UPPER(u.UserName) = UPPER($user);
                """;
            permissionsCommand.Parameters.AddWithValue("$user", resolvedUserName);
            await using var permissionsReader = await permissionsCommand.ExecuteReaderAsync(cancellationToken);
            while (await permissionsReader.ReadAsync(cancellationToken))
                permissions.Add(permissionsReader.GetString(0));
        }

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.CommandText = "UPDATE SystemUsers SET LastLoginAtUtc=$now WHERE UPPER(UserName)=UPPER($user);";
            updateCommand.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            updateCommand.Parameters.AddWithValue("$user", resolvedUserName);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return new AuthenticatedSystemUser(resolvedUserName, displayName, roleName, permissions, isAdmin);
    }
}

internal sealed class SystemLoginWindow : Window
{
    private readonly string _databasePath;
    private readonly TextBox _userName = new() { Text = "Admin" };
    private readonly PasswordBox _password = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _loginButton;

    public AuthenticatedSystemUser? AuthenticatedUser { get; private set; }

    public SystemLoginWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Sign in — AI WordPress Manager";
        Width = 470;
        Height = 430;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var root = new Grid { Margin = new Thickness(34) };
        for (var index = 0; index < 8; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "AI WordPress Manager",
            FontSize = 28,
            FontWeight = FontWeights.Bold
        });

        var description = new TextBlock
        {
            Text = "Sign in to load your role, permissions, and protected workspace.",
            Margin = new Thickness(0, 8, 0, 24),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        var userLabel = new TextBlock { Text = "Username", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(userLabel, 2);
        root.Children.Add(userLabel);
        _userName.Margin = new Thickness(0, 6, 0, 16);
        _userName.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetRow(_userName, 3);
        root.Children.Add(_userName);

        var passwordLabel = new TextBlock { Text = "Password", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(passwordLabel, 4);
        root.Children.Add(passwordLabel);
        _password.Margin = new Thickness(0, 6, 0, 14);
        _password.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetRow(_password, 5);
        root.Children.Add(_password);

        _status.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(_status, 6);
        root.Children.Add(_status);

        _loginButton = new Button
        {
            Content = "Sign in",
            Padding = new Thickness(20, 10, 20, 10),
            FontWeight = FontWeights.Bold,
            IsDefault = true
        };
        _loginButton.Click += async (_, _) => await LoginAsync();
        Grid.SetRow(_loginButton, 7);
        root.Children.Add(_loginButton);

        Content = root;
        Loaded += (_, _) => _password.Focus();
    }

    private async Task LoginAsync()
    {
        _loginButton.IsEnabled = false;
        _status.Text = "Checking account and permissions…";
        try
        {
            var result = await SystemAuthenticationService.AuthenticateAsync(
                _databasePath,
                _userName.Text,
                _password.Password);

            if (result is null)
            {
                _status.Text = "Sign-in failed. Check the username, password, and account status.";
                _password.SelectAll();
                _password.Focus();
                return;
            }

            AuthenticatedUser = result;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            _status.Text = $"Sign-in could not be completed: {exception.Message}";
        }
        finally
        {
            _loginButton.IsEnabled = true;
        }
    }
}
