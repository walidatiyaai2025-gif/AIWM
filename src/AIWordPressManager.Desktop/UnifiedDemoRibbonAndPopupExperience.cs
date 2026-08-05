using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal static class UnifiedDemoRibbonAndPopupExperience
{
    private static DateTime _lastUserActionUtc = DateTime.UtcNow;
    private static readonly string[] AutoPopupTokens =
    [
        "loading", "progress", "operation", "workspace", "review", "journey", "popup"
    ];

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded), true);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnWindowUnloaded), true);
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent,
            new RoutedEventHandler(OnButtonClicked), true);
        EventManager.RegisterClassHandler(typeof(TabControl), Selector.SelectionChangedEvent,
            new SelectionChangedEventHandler(OnTabSelectionChanged), true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
            return;

        window.PreviewMouseDown -= OnUserInput;
        window.PreviewMouseDown += OnUserInput;
        window.PreviewKeyDown -= OnUserKeyInput;
        window.PreviewKeyDown += OnUserKeyInput;

        if (window.GetType().Name == "SiteScopedDemoDataWindow")
        {
            window.Closed -= OnDemoWindowClosed;
            window.Closed += OnDemoWindowClosed;
            return;
        }

        if (ShouldSuppressAutomaticPopup(window))
        {
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window.IsVisible)
                    window.Close();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private static void OnWindowUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;
        window.PreviewMouseDown -= OnUserInput;
        window.PreviewKeyDown -= OnUserKeyInput;
    }

    private static void OnUserInput(object sender, MouseButtonEventArgs e) => _lastUserActionUtc = DateTime.UtcNow;
    private static void OnUserKeyInput(object sender, KeyEventArgs e) => _lastUserActionUtc = DateTime.UtcNow;

    private static bool ShouldSuppressAutomaticPopup(Window window)
    {
        if (window is MainWindow || window is SystemLoginWindow || window.Owner is null)
            return false;

        var name = window.GetType().Name.ToLowerInvariant();
        var title = (window.Title ?? string.Empty).ToLowerInvariant();
        if (name.Contains("error") || title.Contains("error") ||
            name.Contains("confirm") || title.Contains("confirm") ||
            name.Contains("demo") || title.Contains("demo") ||
            name.Contains("site") && title.Contains("add"))
            return false;

        var looksAutomatic = AutoPopupTokens.Any(token => name.Contains(token) || title.Contains(token));
        var initiatedRecently = DateTime.UtcNow - _lastUserActionUtc < TimeSpan.FromSeconds(2.5);
        return looksAutomatic && !initiatedRecently;
    }

    private static async void OnDemoWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;

        try
        {
            var databasePath = ReadPrivate<string>(window, "_databasePath");
            var siteId = ReadPrivate<Guid>(window, "_siteId");
            var siteName = ReadPrivate<string>(window, "_siteName") ?? "Demo Site";
            var siteUrl = ReadPrivate<string>(window, "_siteUrl") ?? "https://demo.local";
            if (string.IsNullOrWhiteSpace(databasePath) || siteId == Guid.Empty)
                return;

            if (!await HasSuccessfulDemoSeedAsync(databasePath, siteId))
                return;

            await LiveWordPressDemoMaterializer.MaterializeAsync(databasePath, siteId, siteName, siteUrl);
            await RefreshVisibleViewModelsAsync(Application.Current.MainWindow);

            MessageBox.Show(Application.Current.MainWindow,
                "Demo data was saved into the live WordPress snapshot tables and all visible screens were refreshed.\n\n" +
                "Posts/Pages: 200\nCategories: 100\nTags: 100\nMedia: 100",
                "Demo Data Applied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow,
                "Demo rows were created, but applying them to the live site snapshot failed.\n\n" + ex.Message,
                "Demo Data Integration",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static T? ReadPrivate<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(instance) is T value) return value;
        return default;
    }

    private static async Task<bool> HasSuccessfulDemoSeedAsync(string databasePath, Guid siteId)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Default Timeout=15");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DemoSeedRuns WHERE SiteId=$siteId AND SeedVersion LIKE '4.%';";
        command.Parameters.AddWithValue("$siteId", siteId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task RefreshVisibleViewModelsAsync(DependencyObject? root)
    {
        if (root is null) return;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var element in EnumerateVisualTree(root))
        {
            if (element is not FrameworkElement { DataContext: { } context } || !visited.Add(context))
                continue;

            foreach (var commandName in new[] { "LoadCommand", "RefreshCommand", "ReloadCommand" })
            {
                var property = context.GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
                if (property?.GetValue(context) is ICommand command && command.CanExecute(null))
                {
                    command.Execute(null);
                    await Task.Delay(25);
                    break;
                }
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        yield return root;
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in EnumerateVisualTree(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    private static void OnButtonClicked(object sender, RoutedEventArgs e)
    {
        _lastUserActionUtc = DateTime.UtcNow;
        if (sender is not Button button || Window.GetWindow(button) is not MainWindow)
            return;

        if (!IsRibbonButton(button)) return;
        var parent = FindAncestor<Panel>(button);
        if (parent is null) return;

        foreach (var sibling in parent.Children.OfType<Button>())
            SetActiveRibbonVisual(sibling, ReferenceEquals(sibling, button));
    }

    private static void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabs || Window.GetWindow(tabs) is not MainWindow)
            return;
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            if (item.IsSelected)
            {
                item.Background = new SolidColorBrush(Color.FromRgb(223, 211, 255));
                item.Foreground = new SolidColorBrush(Color.FromRgb(62, 22, 132));
                item.FontWeight = FontWeights.Bold;
                item.BorderThickness = new Thickness(0, 0, 0, 3);
                item.BorderBrush = new SolidColorBrush(Color.FromRgb(126, 74, 255));
            }
            else
            {
                item.ClearValue(Control.BackgroundProperty);
                item.ClearValue(Control.ForegroundProperty);
                item.FontWeight = FontWeights.SemiBold;
                item.BorderThickness = new Thickness(0);
            }
        }
    }

    private static bool IsRibbonButton(Button button)
    {
        var text = button.Content?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var window = Window.GetWindow(button);
        var point = button.TransformToAncestor(window).Transform(new Point(0, 0));
        return point.Y < 190 && button.ActualHeight <= 70;
    }

    private static void SetActiveRibbonVisual(Button button, bool active)
    {
        if (active)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(223, 211, 255));
            button.Foreground = new SolidColorBrush(Color.FromRgb(62, 22, 132));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(126, 74, 255));
            button.BorderThickness = new Thickness(2);
            button.FontWeight = FontWeights.Bold;
        }
        else
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.ClearValue(Control.BorderBrushProperty);
            button.ClearValue(Control.BorderThicknessProperty);
            button.FontWeight = FontWeights.SemiBold;
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static ReferenceEqualityComparer Instance { get; } = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

internal static class LiveWordPressDemoMaterializer
{
    internal static async Task MaterializeAsync(string databasePath, Guid siteId, string siteName, string siteUrl)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Default Timeout=30");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var site = siteId.ToString("D");
            var now = DateTime.UtcNow.ToString("O");
            var token = Guid.NewGuid().ToByteArray();

            await DeleteDemoRangeAsync(connection, transaction, "WordPressContentRecords", site);
            await DeleteDemoRangeAsync(connection, transaction, "WordPressCategoryRecords", site);
            await DeleteDemoRangeAsync(connection, transaction, "WordPressTagRecords", site);
            await DeleteDemoRangeAsync(connection, transaction, "WordPressMediaRecords", site);

            for (var i = 1; i <= 100; i++)
            {
                await InsertContentAsync(connection, transaction, siteId, 900000 + i, "post", siteName, siteUrl, i, now, token);
                await InsertContentAsync(connection, transaction, siteId, 910000 + i, "page", siteName, siteUrl, i, now, token);
                await InsertTaxonomyAsync(connection, transaction, "WordPressCategoryRecords", siteId, 920000 + i, $"Demo Category {i:000}", $"demo-category-{i:000}", i % 25, now, token);
                await InsertTaxonomyAsync(connection, transaction, "WordPressTagRecords", siteId, 930000 + i, $"Demo Tag {i:000}", $"demo-tag-{i:000}", i % 40, now, token);
                await InsertMediaAsync(connection, transaction, siteId, 940000 + i, siteName, siteUrl, i, now, token);
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static async Task DeleteDemoRangeAsync(SqliteConnection c, SqliteTransaction t, string table, string site)
    {
        if (!await TableExistsAsync(c, t, table)) return;
        await using var command = c.CreateCommand();
        command.Transaction = t;
        command.CommandText = $"DELETE FROM \"{table}\" WHERE SiteId=$site AND WordPressId>=900000;";
        command.Parameters.AddWithValue("$site", site);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertContentAsync(SqliteConnection c, SqliteTransaction t, Guid siteId, int wpId, string type, string siteName, string siteUrl, int i, string now, byte[] token)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = """
            INSERT INTO WordPressContentRecords
            (Id,CreatedAtUtc,UpdatedAtUtc,ConcurrencyToken,SiteId,WordPressId,ContentType,Title,Slug,Status,Link,RenderedContent,RenderedExcerpt,ModifiedAtUtc,IsAvailable,LastSynchronizedAtUtc)
            VALUES($id,$now,$now,$token,$site,$wp,$type,$title,$slug,$status,$link,$content,$excerpt,$now,1,$now);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$now", now); cmd.Parameters.AddWithValue("$token", token);
        cmd.Parameters.AddWithValue("$site", siteId.ToString("D")); cmd.Parameters.AddWithValue("$wp", wpId);
        cmd.Parameters.AddWithValue("$type", type); cmd.Parameters.AddWithValue("$title", $"{siteName} Demo {type} {i:000}");
        cmd.Parameters.AddWithValue("$slug", $"demo-{type}-{i:000}"); cmd.Parameters.AddWithValue("$status", i % 4 == 0 ? "draft" : "publish");
        cmd.Parameters.AddWithValue("$link", $"{siteUrl.TrimEnd('/')}/demo-{type}-{i:000}/");
        cmd.Parameters.AddWithValue("$content", $"<p>Demo {type} content {i:000} for {siteName}.</p>");
        cmd.Parameters.AddWithValue("$excerpt", $"Demo excerpt {i:000} for {siteName}.");
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertTaxonomyAsync(SqliteConnection c, SqliteTransaction t, string table, Guid siteId, int wpId, string name, string slug, int count, string now, byte[] token)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = $"INSERT INTO \"{table}\" (Id,CreatedAtUtc,UpdatedAtUtc,ConcurrencyToken,SiteId,WordPressId,Name,Slug,PostCount,IsAvailable,LastSynchronizedAtUtc) VALUES($id,$now,$now,$token,$site,$wp,$name,$slug,$count,1,$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D")); cmd.Parameters.AddWithValue("$now", now); cmd.Parameters.AddWithValue("$token", token);
        cmd.Parameters.AddWithValue("$site", siteId.ToString("D")); cmd.Parameters.AddWithValue("$wp", wpId); cmd.Parameters.AddWithValue("$name", name); cmd.Parameters.AddWithValue("$slug", slug); cmd.Parameters.AddWithValue("$count", count);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertMediaAsync(SqliteConnection c, SqliteTransaction t, Guid siteId, int wpId, string siteName, string siteUrl, int i, string now, byte[] token)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = """
            INSERT INTO WordPressMediaRecords
            (Id,CreatedAtUtc,UpdatedAtUtc,ConcurrencyToken,SiteId,WordPressId,Title,Slug,MediaType,MimeType,SourceUrl,ModifiedAtUtc,IsAvailable,LastSynchronizedAtUtc)
            VALUES($id,$now,$now,$token,$site,$wp,$title,$slug,'image','image/jpeg',$url,$now,1,$now);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D")); cmd.Parameters.AddWithValue("$now", now); cmd.Parameters.AddWithValue("$token", token);
        cmd.Parameters.AddWithValue("$site", siteId.ToString("D")); cmd.Parameters.AddWithValue("$wp", wpId);
        cmd.Parameters.AddWithValue("$title", $"{siteName} Demo Image {i:000}"); cmd.Parameters.AddWithValue("$slug", $"demo-image-{i:000}");
        cmd.Parameters.AddWithValue("$url", $"{siteUrl.TrimEnd('/')}/wp-content/uploads/demo-image-{i:000}.jpg");
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection c, SqliteTransaction t, string table)
    {
        await using var cmd = c.CreateCommand(); cmd.Transaction = t;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }
}
