using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

internal sealed record RememberedLogin(string UserName, string Password);

internal static class RememberedLoginStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIWordPressManager.RememberMe.v1");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "Security",
        "remembered-login.bin");

    public static void Save(string userName, string password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = JsonSerializer.Serialize(new RememberedLogin(userName.Trim(), password));
        var plain = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, encrypted);
        CryptographicOperations.ZeroMemory(plain);
    }

    public static RememberedLogin? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var encrypted = File.ReadAllBytes(FilePath);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return JsonSerializer.Deserialize<RememberedLogin>(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch
        {
            Clear();
            return null;
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}

internal static class SystemDemoDataSeeder
{
    public static async Task<string> RefreshAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await UserSecurityStore.EnsureCreatedAsync(databasePath);
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS DemoSeedRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SeedVersion TEXT NOT NULL,
                SeededAtUtc TEXT NOT NULL,
                Summary TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS DemoSites (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                BaseUrl TEXT NOT NULL,
                Status TEXT NOT NULL,
                SeoScore INTEGER NOT NULL,
                LastSyncAtUtc TEXT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 1
            );
            DELETE FROM DemoSites WHERE IsDemo=1;
            INSERT INTO DemoSites(Name,BaseUrl,Status,SeoScore,LastSyncAtUtc,IsDemo) VALUES
              ('Demo Travel Blog','https://travel.demo.local','Connected',88,$now,1),
              ('Demo Store','https://store.demo.local','NeedsReview',72,$now,1),
              ('Demo Corporate Site','https://company.demo.local','Connected',94,$now,1);
            """,
            """
            CREATE TABLE IF NOT EXISTS DemoPosts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SiteName TEXT NOT NULL,
                Title TEXT NOT NULL,
                Status TEXT NOT NULL,
                SeoScore INTEGER NOT NULL,
                PublishedAtUtc TEXT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 1
            );
            DELETE FROM DemoPosts WHERE IsDemo=1;
            INSERT INTO DemoPosts(SiteName,Title,Status,SeoScore,PublishedAtUtc,IsDemo) VALUES
              ('Demo Travel Blog','أفضل 10 وجهات صيفية','Published',91,$now,1),
              ('Demo Travel Blog','دليل السفر الاقتصادي','Draft',78,NULL,1),
              ('Demo Store','كيفية اختيار المنتج المناسب','NeedsReview',69,NULL,1),
              ('Demo Corporate Site','خدمات التحول الرقمي','Published',95,$now,1);
            """,
            """
            CREATE TABLE IF NOT EXISTS DemoOperations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Module TEXT NOT NULL,
                ActionName TEXT NOT NULL,
                State TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                Details TEXT NOT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 1
            );
            DELETE FROM DemoOperations WHERE IsDemo=1;
            INSERT INTO DemoOperations(Module,ActionName,State,CreatedAtUtc,Details,IsDemo) VALUES
              ('SEO','Optimize title and meta description','Approved',$now,'Ready for execution',1),
              ('Media','Compress oversized images','Pending',$now,'12 images detected',1),
              ('Links','Repair broken internal links','Queued',$now,'5 broken links',1),
              ('Content','Generate monthly content plan','Completed',$now,'20 article ideas generated',1),
              ('Backup','Create pre-change backup','Completed',$now,'Demo backup verified',1);
            """,
            """
            CREATE TABLE IF NOT EXISTS DemoNotifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Severity TEXT NOT NULL,
                Title TEXT NOT NULL,
                Message TEXT NOT NULL,
                IsRead INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                IsDemo INTEGER NOT NULL DEFAULT 1
            );
            DELETE FROM DemoNotifications WHERE IsDemo=1;
            INSERT INTO DemoNotifications(Severity,Title,Message,IsRead,CreatedAtUtc,IsDemo) VALUES
              ('Info','Demo data ready','All demo modules were refreshed.',0,$now,1),
              ('Warning','SEO review required','One demo site has recommendations awaiting approval.',0,$now,1),
              ('Success','Backup verified','The demo backup and restore workflow is ready to test.',1,$now,1);
            """
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO DemoSeedRuns(SeedVersion,SeededAtUtc,Summary) VALUES('1.0',$now,$summary);";
            audit.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            audit.Parameters.AddWithValue("$summary", "3 sites, 4 posts, 5 operations, and 3 notifications refreshed.");
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return "Demo data refreshed: 3 sites, 4 posts, 5 operations, and 3 notifications.";
    }
}

internal sealed class SystemLoginWindow : Window
{
    private readonly string _databasePath;
    private readonly TextBox _userName = new() { Text = "Admin" };
    private readonly PasswordBox _password = new();
    private readonly CheckBox _rememberMe = new() { Content = "Remember me", Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _loginButton;
    private readonly Button _demoButton;
    private bool _autoLoginAttempted;

    public AuthenticatedSystemUser? AuthenticatedUser { get; private set; }

    public SystemLoginWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Sign in — AI WordPress Manager";
        Width = 500;
        Height = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var root = new Grid { Margin = new Thickness(34) };
        for (var index = 0; index < 10; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "AI WordPress Manager", FontSize = 28, FontWeight = FontWeights.Bold });

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
        _password.Margin = new Thickness(0, 6, 0, 10);
        _password.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetRow(_password, 5);
        root.Children.Add(_password);

        Grid.SetRow(_rememberMe, 6);
        root.Children.Add(_rememberMe);

        _status.Margin = new Thickness(0, 0, 0, 14);
        Grid.SetRow(_status, 7);
        root.Children.Add(_status);

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _loginButton = new Button
        {
            Content = "Sign in",
            Padding = new Thickness(20, 10, 20, 10),
            FontWeight = FontWeights.Bold,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _loginButton.Click += async (_, _) => await LoginAsync();
        actions.Children.Add(_loginButton);

        _demoButton = new Button
        {
            Content = "Create / Refresh Demo Data",
            Padding = new Thickness(14, 10, 14, 10),
            ToolTip = "Creates repeatable demo records for testing the system. Running it again safely refreshes the demo records."
        };
        _demoButton.Click += async (_, _) => await RefreshDemoDataAsync();
        Grid.SetColumn(_demoButton, 1);
        actions.Children.Add(_demoButton);

        Grid.SetRow(actions, 8);
        root.Children.Add(actions);

        var demoNote = new TextBlock
        {
            Text = "Demo data is idempotent: run it again after upgrades to refresh test records.",
            Margin = new Thickness(0, 12, 0, 0),
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(demoNote, 9);
        root.Children.Add(demoNote);

        Content = root;
        Loaded += async (_, _) => await LoadRememberedLoginAsync();
    }

    private async Task LoadRememberedLoginAsync()
    {
        if (_autoLoginAttempted) return;
        _autoLoginAttempted = true;

        var remembered = RememberedLoginStore.Load();
        if (remembered is null)
        {
            _password.Focus();
            return;
        }

        _userName.Text = remembered.UserName;
        _password.Password = remembered.Password;
        _rememberMe.IsChecked = true;
        _status.Text = "Signing in with your remembered account…";
        await LoginAsync();
    }

    private async Task LoginAsync()
    {
        _loginButton.IsEnabled = false;
        _demoButton.IsEnabled = false;
        _status.Text = "Checking account and permissions…";
        try
        {
            var result = await SystemAuthenticationService.AuthenticateAsync(_databasePath, _userName.Text, _password.Password);
            if (result is null)
            {
                RememberedLoginStore.Clear();
                _rememberMe.IsChecked = false;
                _status.Text = "Sign-in failed. Check the username, password, and account status.";
                _password.SelectAll();
                _password.Focus();
                return;
            }

            if (_rememberMe.IsChecked == true)
                RememberedLoginStore.Save(result.UserName, _password.Password);
            else
                RememberedLoginStore.Clear();

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
            _demoButton.IsEnabled = true;
        }
    }

    private async Task RefreshDemoDataAsync()
    {
        _loginButton.IsEnabled = false;
        _demoButton.IsEnabled = false;
        _status.Text = "Creating and refreshing demo data…";
        try
        {
            _status.Text = await SystemDemoDataSeeder.RefreshAsync(_databasePath);
        }
        catch (Exception exception)
        {
            _status.Text = $"Demo data could not be created: {exception.Message}";
        }
        finally
        {
            _loginButton.IsEnabled = true;
            _demoButton.IsEnabled = true;
        }
    }
}
