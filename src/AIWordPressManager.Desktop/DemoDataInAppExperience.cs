using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal sealed record DemoSeedProgress(int Percent, string Stage, string Details, int RecordsCreated);

internal static class DemoDataInAppExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;

        if (window is SystemLoginWindow)
        {
            HideLoginDemoControls(window);
            return;
        }

        if (window is MainWindow mainWindow)
            AddDemoButton(mainWindow);
    }

    private static void HideLoginDemoControls(Window loginWindow)
    {
        foreach (var button in FindVisualChildren<Button>(loginWindow))
        {
            if (button.Content?.ToString()?.Contains("Demo Data", StringComparison.OrdinalIgnoreCase) == true)
                button.Visibility = Visibility.Collapsed;
        }

        foreach (var text in FindVisualChildren<TextBlock>(loginWindow))
        {
            if (text.Text.Contains("Demo data is idempotent", StringComparison.OrdinalIgnoreCase))
                text.Visibility = Visibility.Collapsed;
        }
    }

    private static void AddDemoButton(MainWindow window)
    {
        if (window.Content is not Grid root || root.Children.OfType<Button>().Any(x => Equals(x.Tag, "DemoDataLauncher")))
            return;

        var button = new Button
        {
            Tag = "DemoDataLauncher",
            Content = "🧪  Demo Data",
            ToolTip = "Create or refresh test data and watch the actual SQLite progress",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 5, 112, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Panel = { ZIndex = 5000 }
        };

        button.Click += (_, _) =>
        {
            var databasePath = window.Tag as string;
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                MessageBox.Show(window, "Database path is not available yet.", "Demo Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            new DemoDataProgressWindow(databasePath) { Owner = window }.ShowDialog();
        };

        Grid.SetRow(button, 0);
        Grid.SetColumnSpan(button, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D) yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
}

internal sealed class DemoDataProgressWindow : Window
{
    private readonly string _databasePath;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 24 };
    private readonly TextBlock _stage = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _records = new() { FontWeight = FontWeights.SemiBold };
    private readonly Button _startButton = new() { Content = "Create / Refresh Demo Data", Padding = new Thickness(18, 10, 18, 10), IsDefault = true };
    private readonly Button _closeButton = new() { Content = "Close", Padding = new Thickness(18, 10, 18, 10), IsCancel = true };

    internal DemoDataProgressWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Demo Data Generator";
        Width = 650;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(28) };
        for (var i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "System Demo Data", FontSize = 26, FontWeight = FontWeights.Bold };
        root.Children.Add(title);

        var intro = new TextBlock
        {
            Text = "Creates repeatable test records in the real local SQLite database. Progress advances only after each database stage succeeds.",
            Margin = new Thickness(0, 8, 0, 22), TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        };
        Grid.SetRow(intro, 1); root.Children.Add(intro);

        Grid.SetRow(_stage, 2); root.Children.Add(_stage);
        _details.Margin = new Thickness(0, 6, 0, 14); Grid.SetRow(_details, 3); root.Children.Add(_details);
        Grid.SetRow(_progress, 4); root.Children.Add(_progress);
        _records.Margin = new Thickness(0, 10, 0, 18); Grid.SetRow(_records, 5); root.Children.Add(_records);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _startButton.Margin = new Thickness(0, 0, 10, 0);
        _startButton.Click += async (_, _) => await RunAsync();
        actions.Children.Add(_startButton); actions.Children.Add(_closeButton);
        Grid.SetRow(actions, 6); root.Children.Add(actions);

        Content = root;
        SetProgress(new DemoSeedProgress(0, "Ready", "Press the button to start.", 0));
    }

    private async Task RunAsync()
    {
        _startButton.IsEnabled = false;
        _closeButton.IsEnabled = false;
        try
        {
            var progress = new Progress<DemoSeedProgress>(SetProgress);
            var summary = await ProgressiveDemoDataSeeder.RefreshAsync(_databasePath, progress);
            SetProgress(new DemoSeedProgress(100, "Completed", summary, 15));
        }
        catch (Exception exception)
        {
            _stage.Text = "Failed";
            _details.Text = exception.Message;
            MessageBox.Show(this, exception.ToString(), "Demo Data Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _startButton.IsEnabled = true;
            _closeButton.IsEnabled = true;
        }
    }

    private void SetProgress(DemoSeedProgress value)
    {
        _progress.Value = value.Percent;
        _stage.Text = $"{value.Percent}% — {value.Stage}";
        _details.Text = value.Details;
        _records.Text = $"Records created/refreshed: {value.RecordsCreated}";
    }
}

internal static class ProgressiveDemoDataSeeder
{
    internal static async Task<string> RefreshAsync(string databasePath, IProgress<DemoSeedProgress> progress, CancellationToken cancellationToken = default)
    {
        await UserSecurityStore.EnsureCreatedAsync(databasePath);
        progress.Report(new(5, "Opening database", databasePath, 0));

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var now = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var records = 0;

        await ExecuteStageAsync(connection, transaction, "Creating demo schema", 15, "Preparing demo tables.", 0, """
            CREATE TABLE IF NOT EXISTS DemoSeedRuns(Id INTEGER PRIMARY KEY AUTOINCREMENT,SeedVersion TEXT NOT NULL,SeededAtUtc TEXT NOT NULL,Summary TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DemoSites(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,BaseUrl TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,LastSyncAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS DemoPosts(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteName TEXT NOT NULL,Title TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,PublishedAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS DemoOperations(Id INTEGER PRIMARY KEY AUTOINCREMENT,Module TEXT NOT NULL,ActionName TEXT NOT NULL,State TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,Details TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS DemoNotifications(Id INTEGER PRIMARY KEY AUTOINCREMENT,Severity TEXT NOT NULL,Title TEXT NOT NULL,Message TEXT NOT NULL,IsRead INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);
            """, now, progress, cancellationToken);

        await ExecuteStageAsync(connection, transaction, "Refreshing sites", 35, "Writing 3 demo WordPress sites.", records += 3, """
            DELETE FROM DemoSites WHERE IsDemo=1;
            INSERT INTO DemoSites(Name,BaseUrl,Status,SeoScore,LastSyncAtUtc,IsDemo) VALUES
            ('Demo Travel Blog','https://travel.demo.local','Connected',88,$now,1),('Demo Store','https://store.demo.local','NeedsReview',72,$now,1),('Demo Corporate Site','https://company.demo.local','Connected',94,$now,1);
            """, now, progress, cancellationToken);

        await ExecuteStageAsync(connection, transaction, "Refreshing posts", 55, "Writing 4 demo posts and SEO scores.", records += 4, """
            DELETE FROM DemoPosts WHERE IsDemo=1;
            INSERT INTO DemoPosts(SiteName,Title,Status,SeoScore,PublishedAtUtc,IsDemo) VALUES
            ('Demo Travel Blog','أفضل 10 وجهات صيفية','Published',91,$now,1),('Demo Travel Blog','دليل السفر الاقتصادي','Draft',78,NULL,1),('Demo Store','كيفية اختيار المنتج المناسب','NeedsReview',69,NULL,1),('Demo Corporate Site','خدمات التحول الرقمي','Published',95,$now,1);
            """, now, progress, cancellationToken);

        await ExecuteStageAsync(connection, transaction, "Refreshing operations", 75, "Writing 5 workflow and execution records.", records += 5, """
            DELETE FROM DemoOperations WHERE IsDemo=1;
            INSERT INTO DemoOperations(Module,ActionName,State,CreatedAtUtc,Details,IsDemo) VALUES
            ('SEO','Optimize title and meta description','Approved',$now,'Ready for execution',1),('Media','Compress oversized images','Pending',$now,'12 images detected',1),('Links','Repair broken internal links','Queued',$now,'5 broken links',1),('Content','Generate monthly content plan','Completed',$now,'20 article ideas generated',1),('Backup','Create pre-change backup','Completed',$now,'Demo backup verified',1);
            """, now, progress, cancellationToken);

        await ExecuteStageAsync(connection, transaction, "Refreshing notifications", 90, "Writing 3 system notifications.", records += 3, """
            DELETE FROM DemoNotifications WHERE IsDemo=1;
            INSERT INTO DemoNotifications(Severity,Title,Message,IsRead,CreatedAtUtc,IsDemo) VALUES
            ('Info','Demo data ready','All demo modules were refreshed.',0,$now,1),('Warning','SEO review required','One demo site has recommendations awaiting approval.',0,$now,1),('Success','Backup verified','The demo backup and restore workflow is ready to test.',1,$now,1);
            """, now, progress, cancellationToken);

        progress.Report(new(96, "Committing transaction", "Saving all completed stages atomically.", records));
        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO DemoSeedRuns(SeedVersion,SeededAtUtc,Summary) VALUES('2.0',$now,$summary);";
            audit.Parameters.AddWithValue("$now", now);
            audit.Parameters.AddWithValue("$summary", $"{records} demo records refreshed with progressive database tracking.");
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return $"Database commit completed. {records} demo records were created or refreshed.";
    }

    private static async Task ExecuteStageAsync(SqliteConnection connection, SqliteTransaction transaction, string stage, int percent, string details, int records, string sql, string now, IProgress<DemoSeedProgress> progress, CancellationToken cancellationToken)
    {
        progress.Report(new(percent - 8, stage, details, records));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        progress.Report(new(percent, stage + " completed", details, records));
    }
}
