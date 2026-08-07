using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private IAsyncRelayCommand? _manageUsersCommand;
        public IAsyncRelayCommand ManageUsersCommand =>
            _manageUsersCommand ??= new AsyncRelayCommand(OpenUserAdministrationAsync, () => !IsOperationRunning);

        private async Task OpenUserAdministrationAsync()
        {
            var databasePath = _applicationPaths.GetDatabasePath();
            await UserSecurityStore.EnsureCreatedAsync(databasePath);
            var window = new UserAdministrationWindow(databasePath)
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            window.ShowDialog();
        }
    }
}

namespace AIWordPressManager.Desktop
{
    internal sealed record SystemUserRow(long Id, string UserName, string DisplayName, string RoleName, bool IsActive, bool IsSystemAdmin, DateTime CreatedAtUtc);

    internal static class UserSecurityStore
    {
        private static readonly string[] PermissionKeys =
        [
            "Dashboard.View", "Sites.Manage", "WordPress.Sync", "Content.View", "Content.Edit",
            "Seo.Audit", "Suggestions.Generate", "Suggestions.Approve", "Execution.Run", "Execution.Rollback",
            "Backups.Manage", "Reports.View", "Logs.View", "Settings.Manage", "Users.Manage",
            "Roles.Manage", "Database.Reset", "System.Administration"
        ];

        public static async Task EnsureCreatedAsync(string databasePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();

            var sql = """
                CREATE TABLE IF NOT EXISTS SystemRoles (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Description TEXT NOT NULL DEFAULT '',
                    IsSystemRole INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS SystemPermissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PermissionKey TEXT NOT NULL UNIQUE,
                    Description TEXT NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS SystemUsers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserName TEXT NOT NULL UNIQUE,
                    DisplayName TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    PasswordSalt TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    IsSystemAdmin INTEGER NOT NULL DEFAULT 0,
                    CreatedAtUtc TEXT NOT NULL,
                    LastLoginAtUtc TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS SystemUserRoles (
                    UserId INTEGER NOT NULL,
                    RoleId INTEGER NOT NULL,
                    PRIMARY KEY (UserId, RoleId),
                    FOREIGN KEY (UserId) REFERENCES SystemUsers(Id) ON DELETE CASCADE,
                    FOREIGN KEY (RoleId) REFERENCES SystemRoles(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS SystemRolePermissions (
                    RoleId INTEGER NOT NULL,
                    PermissionId INTEGER NOT NULL,
                    PRIMARY KEY (RoleId, PermissionId),
                    FOREIGN KEY (RoleId) REFERENCES SystemRoles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (PermissionId) REFERENCES SystemPermissions(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS SystemAuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserName TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    Detail TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                """;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }

            await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemRoles(Name, Description, IsSystemRole, CreatedAtUtc) VALUES ('Admin', 'Full system administration', 1, $now), ('Manager', 'Manage sites, content, approvals and reports', 1, $now), ('Operator', 'Run synchronization and approved operations', 1, $now), ('Viewer', 'Read-only access', 1, $now);", ("$now", DateTime.UtcNow.ToString("O")));

            foreach (var permission in PermissionKeys)
                await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemPermissions(PermissionKey, Description) VALUES ($key, $description);", ("$key", permission), ("$description", permission.Replace('.', ' ')));

            await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemRolePermissions(RoleId, PermissionId) SELECT r.Id, p.Id FROM SystemRoles r CROSS JOIN SystemPermissions p WHERE r.Name='Admin';");
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemRolePermissions(RoleId, PermissionId) SELECT r.Id, p.Id FROM SystemRoles r CROSS JOIN SystemPermissions p WHERE r.Name='Viewer' AND p.PermissionKey IN ('Dashboard.View','Content.View','Reports.View');");
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemRolePermissions(RoleId, PermissionId) SELECT r.Id, p.Id FROM SystemRoles r CROSS JOIN SystemPermissions p WHERE r.Name='Operator' AND p.PermissionKey IN ('Dashboard.View','WordPress.Sync','Content.View','Seo.Audit','Execution.Run','Backups.Manage','Reports.View');");
            await ExecuteAsync(connection, "INSERT OR IGNORE INTO SystemRolePermissions(RoleId, PermissionId) SELECT r.Id, p.Id FROM SystemRoles r CROSS JOIN SystemPermissions p WHERE r.Name='Manager' AND p.PermissionKey NOT IN ('Users.Manage','Roles.Manage','Database.Reset','System.Administration');");

            var adminExists = await ScalarLongAsync(connection, "SELECT COUNT(*) FROM SystemUsers WHERE IsSystemAdmin=1;") > 0;
            if (!adminExists)
            {
                var salt = RandomNumberGenerator.GetBytes(32);
                var hash = HashPassword("Admin@123", salt);
                await ExecuteAsync(connection,
                    "INSERT INTO SystemUsers(UserName, DisplayName, PasswordHash, PasswordSalt, IsActive, IsSystemAdmin, CreatedAtUtc) VALUES ('Admin','System Administrator',$hash,$salt,1,1,$now);",
                    ("$hash", Convert.ToBase64String(hash)), ("$salt", Convert.ToBase64String(salt)), ("$now", DateTime.UtcNow.ToString("O")));
                await ExecuteAsync(connection, "INSERT INTO SystemUserRoles(UserId, RoleId) SELECT u.Id, r.Id FROM SystemUsers u CROSS JOIN SystemRoles r WHERE u.UserName='Admin' AND r.Name='Admin';");
                await ExecuteAsync(connection, "INSERT INTO SystemAuditLog(UserName, Action, Detail, CreatedAtUtc) VALUES ('SYSTEM','AdminSeeded','Default administrator created. Password must be changed after first sign-in.',$now);", ("$now", DateTime.UtcNow.ToString("O")));
            }
        }

        public static async Task<IReadOnlyList<SystemUserRow>> LoadUsersAsync(string databasePath)
        {
            await EnsureCreatedAsync(databasePath);
            var rows = new List<SystemUserRow>();
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT u.Id, u.UserName, u.DisplayName, COALESCE(GROUP_CONCAT(r.Name, ', '), 'No role'),
                       u.IsActive, u.IsSystemAdmin, u.CreatedAtUtc
                FROM SystemUsers u
                LEFT JOIN SystemUserRoles ur ON ur.UserId=u.Id
                LEFT JOIN SystemRoles r ON r.Id=ur.RoleId
                GROUP BY u.Id, u.UserName, u.DisplayName, u.IsActive, u.IsSystemAdmin, u.CreatedAtUtc
                ORDER BY u.IsSystemAdmin DESC, u.UserName;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(new SystemUserRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4) == 1, reader.GetInt64(5) == 1, DateTime.Parse(reader.GetString(6))));
            return rows;
        }

        public static async Task AddUserAsync(string databasePath, string userName, string displayName, string password, string role)
        {
            await EnsureCreatedAsync(databasePath);
            var salt = RandomNumberGenerator.GetBytes(32);
            var hash = HashPassword(password, salt);
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO SystemUsers(UserName,DisplayName,PasswordHash,PasswordSalt,IsActive,IsSystemAdmin,CreatedAtUtc) VALUES ($user,$display,$hash,$salt,1,0,$now); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$user", userName.Trim());
            command.Parameters.AddWithValue("$display", displayName.Trim());
            command.Parameters.AddWithValue("$hash", Convert.ToBase64String(hash));
            command.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt));
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
            var id = Convert.ToInt64(await command.ExecuteScalarAsync());
            await using var roleCommand = connection.CreateCommand();
            roleCommand.Transaction = transaction;
            roleCommand.CommandText = "INSERT INTO SystemUserRoles(UserId,RoleId) SELECT $userId, Id FROM SystemRoles WHERE Name=$role;";
            roleCommand.Parameters.AddWithValue("$userId", id);
            roleCommand.Parameters.AddWithValue("$role", role);
            await roleCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        public static async Task ToggleActiveAsync(string databasePath, SystemUserRow user)
        {
            if (user.IsSystemAdmin) return;
            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await ExecuteAsync(connection, "UPDATE SystemUsers SET IsActive=$active WHERE Id=$id AND IsSystemAdmin=0;", ("$active", user.IsActive ? 0 : 1), ("$id", user.Id));
        }

        private static byte[] HashPassword(string password, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 210_000, HashAlgorithmName.SHA256, 32);

        private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
    }

    internal sealed class UserAdministrationWindow : Window
    {
        private readonly string _databasePath;
        private readonly ObservableCollection<SystemUserRow> _users = [];
        private readonly DataGrid _grid;

        public UserAdministrationWindow(string databasePath)
        {
            _databasePath = databasePath;
            Title = "Users, Roles & Permissions";
            Width = 980;
            Height = 650;
            MinWidth = 800;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());

            var title = new TextBlock { Text = "Users, Roles & Permissions", FontSize = 26, FontWeight = FontWeights.Bold };
            root.Children.Add(title);
            var description = new TextBlock
            {
                Text = "Admin has complete system control. Other roles receive only the permissions assigned to them. The default Admin password is Admin@123 and must be changed before production use.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 18),
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(description, 1);
            root.Children.Add(description);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(content, 2);
            root.Children.Add(content);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var add = new Button { Content = "Add User", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
            add.Click += async (_, _) => await AddUserAsync();
            var toggle = new Button { Content = "Enable / Disable", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0) };
            toggle.Click += async (_, _) => await ToggleAsync();
            var refresh = new Button { Content = "Refresh", Padding = new Thickness(18, 8, 18, 8) };
            refresh.Click += async (_, _) => await ReloadAsync();
            buttons.Children.Add(add); buttons.Children.Add(toggle); buttons.Children.Add(refresh);
            content.Children.Add(buttons);

            _grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, ItemsSource = _users };
            _grid.Columns.Add(new DataGridTextColumn { Header = "Username", Binding = new Binding(nameof(SystemUserRow.UserName)), Width = 160 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Display Name", Binding = new Binding(nameof(SystemUserRow.DisplayName)), Width = 220 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Roles", Binding = new Binding(nameof(SystemUserRow.RoleName)), Width = 180 });
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Active", Binding = new Binding(nameof(SystemUserRow.IsActive)), Width = 80 });
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Admin", Binding = new Binding(nameof(SystemUserRow.IsSystemAdmin)), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Created", Binding = new Binding(nameof(SystemUserRow.CreatedAtUtc)) { StringFormat = "g" }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            Grid.SetRow(_grid, 1);
            content.Children.Add(_grid);
            Content = root;
            Loaded += async (_, _) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            var rows = await UserSecurityStore.LoadUsersAsync(_databasePath);
            _users.Clear();
            foreach (var row in rows) _users.Add(row);
        }

        private async Task ToggleAsync()
        {
            if (_grid.SelectedItem is not SystemUserRow user) return;
            if (user.IsSystemAdmin)
            {
                MessageBox.Show("The system Admin account cannot be disabled.", "Protected account", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await UserSecurityStore.ToggleActiveAsync(_databasePath, user);
            await ReloadAsync();
        }

        private async Task AddUserAsync()
        {
            var dialog = new AddUserWindow { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                await UserSecurityStore.AddUserAsync(_databasePath, dialog.UserNameValue, dialog.DisplayNameValue, dialog.PasswordValue, dialog.RoleValue);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Could not add user", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    internal sealed class AddUserWindow : Window
    {
        private readonly TextBox _userName = new();
        private readonly TextBox _displayName = new();
        private readonly PasswordBox _password = new();
        private readonly ComboBox _role = new() { ItemsSource = new[] { "Manager", "Operator", "Viewer" }, SelectedIndex = 2 };
        public string UserNameValue => _userName.Text;
        public string DisplayNameValue => _displayName.Text;
        public string PasswordValue => _password.Password;
        public string RoleValue => _role.SelectedItem?.ToString() ?? "Viewer";

        public AddUserWindow()
        {
            Title = "Add User"; Width = 440; Height = 390; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = "Create a system user", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18) });
            AddField(panel, "Username", _userName); AddField(panel, "Display name", _displayName); AddField(panel, "Temporary password", _password); AddField(panel, "Role", _role);
            var save = new Button { Content = "Create User", Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(UserNameValue) || string.IsNullOrWhiteSpace(DisplayNameValue) || PasswordValue.Length < 8)
                {
                    MessageBox.Show("Enter username, display name, and a password of at least 8 characters.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
            };
            panel.Children.Add(save); Content = panel;
        }

        private static void AddField(Panel panel, string label, Control control)
        {
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) });
            control.MinHeight = 32; panel.Children.Add(control);
        }
    }

    internal static class UserAdministrationBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(Install));
        }

        private static void Install(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var systemTab = FindVisualChild<TabItem>(window, item => string.Equals(item.Header?.ToString(), "SYSTEM", StringComparison.Ordinal));
                if (systemTab?.Content is not StackPanel panel) return;
                var adminGroup = panel.Children.OfType<Border>().FirstOrDefault(x => x.Tag?.ToString() == "AdministrationTools");
                var stack = adminGroup?.Child as StackPanel;
                if (stack is null || stack.Children.OfType<Button>().Any(x => x.Tag?.ToString() == "UsersRoles")) return;
                var button = new Button { Tag = "UsersRoles", Content = "👥\nUsers & Roles", MinWidth = 96, MinHeight = 58, Padding = new Thickness(10, 6, 10, 6), ToolTip = "Manage users, roles, permissions, activation, and protected Admin access." };
                if (window.TryFindResource("RibbonLargeButtonStyle") is Style style) button.Style = style;
                button.SetBinding(Button.CommandProperty, new Binding("ManageUsersCommand"));
                stack.Children.Add(button);
            }));
        }

        private static T? FindVisualChild<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed && predicate(typed)) return typed;
                var nested = FindVisualChild(child, predicate);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
