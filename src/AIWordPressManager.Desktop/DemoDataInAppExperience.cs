using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace AIWordPressManager.Desktop;

internal sealed record DemoSeedProgress(
    int CompletedOperations,
    int TotalOperations,
    string Stage,
    string Details,
    string LogLine,
    int RecordsCreated,
    bool IsCompleted = false,
    bool IsError = false)
{
    public double Percent => TotalOperations <= 0
        ? 0
        : Math.Clamp(CompletedOperations * 100d / TotalOperations, 0, 100);
}

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
        if (sender is not Window window)
            return;

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
        if (window.Content is not Grid root ||
            root.Children.OfType<Button>().Any(x => Equals(x.Tag, "DemoDataLauncher")))
        {
            return;
        }

        var button = new Button
        {
            Tag = "DemoDataLauncher",
            Content = "🧪  Demo Data",
            ToolTip = "Create or refresh at least 100 test records per demo table",
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 5, 112, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Panel.SetZIndex(button, 5000);

        button.Click += (_, _) =>
        {
            var databasePath = window.Tag as string;
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                MessageBox.Show(
                    window,
                    "Database path is not available yet.",
                    "Demo Data",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            new DemoDataProgressWindow(databasePath) { Owner = window }.ShowDialog();
        };

        Grid.SetRow(button, 0);
        Grid.SetColumnSpan(button, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is not Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }
}

internal sealed class DemoDataProgressWindow : Window
{
    private readonly string _databasePath;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 24 };
    private readonly TextBlock _stage = new()
    {
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.78 };
    private readonly TextBlock _records = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _elapsed = new();
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        MinHeight = 250
    };
    private readonly Button _startButton = new()
    {
        Content = "Create / Refresh Demo Data",
        Padding = new Thickness(18, 10, 18, 10),
        IsDefault = true
    };
    private readonly Button _copyButton = new() { Content = "Copy Log", Padding = new Thickness(14, 8, 14, 8) };
    private readonly Button _saveButton = new() { Content = "Save Log", Padding = new Thickness(14, 8, 14, 8) };
    private readonly Button _clearButton = new() { Content = "Clear Log", Padding = new Thickness(14, 8, 14, 8) };
    private readonly Button _closeButton = new()
    {
        Content = "Close",
        Padding = new Thickness(18, 10, 18, 10),
        IsCancel = true
    };
    private readonly Stopwatch _stopwatch = new();

    internal DemoDataProgressWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "Demo Data Generator";
        Width = 880;
        Height = 720;
        MinWidth = 760;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "System Demo Data",
            FontSize = 26,
            FontWeight = FontWeights.Bold
        });

        var intro = new TextBlock
        {
            Text = "Creates 100 records in every demo table. The progress value changes only after a real SQLite operation succeeds.",
            Margin = new Thickness(0, 8, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
        Grid.SetRow(intro, 1);
        root.Children.Add(intro);

        Grid.SetRow(_stage, 2);
        root.Children.Add(_stage);

        _details.Margin = new Thickness(0, 5, 0, 10);
        Grid.SetRow(_details, 3);
        root.Children.Add(_details);

        Grid.SetRow(_progress, 4);
        root.Children.Add(_progress);

        var counters = new Grid { Margin = new Thickness(0, 8, 0, 10) };
        counters.ColumnDefinitions.Add(new ColumnDefinition());
        counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        counters.Children.Add(_records);
        Grid.SetColumn(_elapsed, 1);
        counters.Children.Add(_elapsed);
        Grid.SetRow(counters, 5);
        root.Children.Add(counters);

        Grid.SetRow(_log, 6);
        root.Children.Add(_log);

        var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _copyButton.Margin = new Thickness(0, 0, 8, 0);
        _saveButton.Margin = new Thickness(0, 0, 8, 0);
        _clearButton.Margin = new Thickness(0, 0, 8, 0);
        _startButton.Margin = new Thickness(0, 0, 10, 0);

        _copyButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_log.Text))
                Clipboard.SetText(_log.Text);
        };
        _saveButton.Click += (_, _) => SaveLog();
        _clearButton.Click += (_, _) => _log.Clear();
        _startButton.Click += async (_, _) => await RunAsync();

        actions.Children.Add(_copyButton);
        Grid.SetColumn(_saveButton, 1);
        actions.Children.Add(_saveButton);
        Grid.SetColumn(_clearButton, 2);
        actions.Children.Add(_clearButton);
        Grid.SetColumn(_startButton, 4);
        actions.Children.Add(_startButton);
        Grid.SetColumn(_closeButton, 5);
        actions.Children.Add(_closeButton);

        Grid.SetRow(actions, 7);
        root.Children.Add(actions);

        Content = root;
        SetProgress(new DemoSeedProgress(0, ProgressiveDemoDataSeeder.TotalRecordOperations, "Ready", "Press Create / Refresh Demo Data to start.", "Ready.", 0));
    }

    private async Task RunAsync()
    {
        _startButton.IsEnabled = false;
        _closeButton.IsEnabled = false;
        _log.Clear();
        _stopwatch.Restart();

        try
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] START | Database: {_databasePath}");
            var progress = new Progress<DemoSeedProgress>(SetProgress);
            var summary = await ProgressiveDemoDataSeeder.RefreshAsync(_databasePath, progress);
            SetProgress(new DemoSeedProgress(
                ProgressiveDemoDataSeeder.TotalRecordOperations,
                ProgressiveDemoDataSeeder.TotalRecordOperations,
                "Completed successfully",
                summary,
                $"SUCCESS | {summary}",
                ProgressiveDemoDataSeeder.TotalRecordOperations,
                IsCompleted: true));
        }
        catch (Exception exception)
        {
            SetProgress(new DemoSeedProgress(
                (int)Math.Round(_progress.Value * ProgressiveDemoDataSeeder.TotalRecordOperations / 100d),
                ProgressiveDemoDataSeeder.TotalRecordOperations,
                "Failed",
                exception.Message,
                $"ERROR | {exception.GetType().Name}: {exception.Message}",
                ParseCreatedRecordCount(),
                IsError: true));
            MessageBox.Show(this, exception.ToString(), "Demo Data Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _stopwatch.Stop();
            _elapsed.Text = $"Elapsed: {_stopwatch.Elapsed:hh\:mm\:ss}";
            _startButton.IsEnabled = true;
            _closeButton.IsEnabled = true;
        }
    }

    private int ParseCreatedRecordCount()
    {
        var text = _records.Text;
        var digits = new string(text.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

    private void SetProgress(DemoSeedProgress value)
    {
        _progress.Value = value.Percent;
        _stage.Text = $"{value.Percent:N1}% — {value.Stage}";
        _details.Text = value.Details;
        _records.Text = $"Records created/refreshed: {value.RecordsCreated} / {value.TotalOperations}";
        _elapsed.Text = $"Elapsed: {_stopwatch.Elapsed:hh\:mm\:ss}";

        if (!string.IsNullOrWhiteSpace(value.LogLine))
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {value.LogLine}");
    }

    private void AppendLog(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private void SaveLog()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Demo Data Log",
            Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt",
            FileName = $"demo-data-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };

        if (dialog.ShowDialog(this) == true)
            System.IO.File.WriteAllText(dialog.FileName, _log.Text, Encoding.UTF8);
    }
}

internal static class ProgressiveDemoDataSeeder
{
    internal const int RecordsPerTable = 100;
    internal const int TableCount = 10;
    internal const int TotalRecordOperations = RecordsPerTable * TableCount;

    private sealed record DemoTableDefinition(
        string Name,
        string CreateSql,
        string DeleteSql,
        string InsertSql,
        Action<SqliteCommand, int, string> BindParameters);

    internal static async Task<string> RefreshAsync(
        string databasePath,
        IProgress<DemoSeedProgress> progress,
        CancellationToken cancellationToken = default)
    {
        await UserSecurityStore.EnsureCreatedAsync(databasePath);
        progress.Report(new(0, TotalRecordOperations, "Opening database", databasePath, "OPEN | Opening SQLite database.", 0));

        await using var connection = new SqliteConnection($"Data Source={databasePath};Default Timeout=30");
        await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var tables = CreateDefinitions();

        progress.Report(new(0, TotalRecordOperations, "Preparing schema", $"Creating {tables.Count} demo tables.", "SCHEMA | Starting table creation.", 0));

        await using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteNonQueryAsync(connection, transaction, table.CreateSql, cancellationToken);
                await ExecuteNonQueryAsync(connection, transaction, table.DeleteSql, cancellationToken);
                progress.Report(new(0, TotalRecordOperations, "Preparing schema", $"{table.Name}: table ready and old demo rows removed.", $"TABLE READY | {table.Name}", 0));
            }

            var completed = 0;
            foreach (var table in tables)
            {
                progress.Report(new(completed, TotalRecordOperations, $"Populating {table.Name}", $"Inserting {RecordsPerTable} records.", $"START TABLE | {table.Name} | target={RecordsPerTable}", completed));

                for (var index = 1; index <= RecordsPerTable; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = table.InsertSql;
                    table.BindParameters(command, index, now);
                    var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                    if (affected != 1)
                        throw new InvalidOperationException($"{table.Name} row {index} was not inserted. Affected rows: {affected}.");

                    completed++;
                    progress.Report(new(
                        completed,
                        TotalRecordOperations,
                        $"Populating {table.Name}",
                        $"Inserted row {index} of {RecordsPerTable} into {table.Name}.",
                        $"INSERT OK | {table.Name} | row={index}/{RecordsPerTable} | total={completed}/{TotalRecordOperations}",
                        completed));

                    if (index % 10 == 0)
                        await Task.Yield();
                }

                progress.Report(new(completed, TotalRecordOperations, $"{table.Name} completed", $"Verified {RecordsPerTable} inserted rows.", $"TABLE DONE | {table.Name} | inserted={RecordsPerTable}", completed));
            }

            await using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = """
                    CREATE TABLE IF NOT EXISTS DemoSeedRuns(
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SeedVersion TEXT NOT NULL,
                        SeededAtUtc TEXT NOT NULL,
                        Summary TEXT NOT NULL);
                    INSERT INTO DemoSeedRuns(SeedVersion,SeededAtUtc,Summary)
                    VALUES('3.0',$now,$summary);
                    """;
                audit.Parameters.AddWithValue("$now", now);
                audit.Parameters.AddWithValue("$summary", $"{TableCount} tables and {TotalRecordOperations} demo records refreshed.");
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            progress.Report(new(TotalRecordOperations, TotalRecordOperations, "Committing transaction", "All inserts succeeded. Committing SQLite transaction now.", "COMMIT START | All records are ready.", TotalRecordOperations));

            // SQLite commit is deliberately synchronous here. It avoids the provider's
            // DbTransaction async path that previously left the UI waiting at 96%.
            transaction.Commit();

            progress.Report(new(TotalRecordOperations, TotalRecordOperations, "Commit completed", "SQLite confirmed the transaction commit.", "COMMIT OK | Transaction saved successfully.", TotalRecordOperations, IsCompleted: true));
            return $"Created {TotalRecordOperations} records across {TableCount} tables ({RecordsPerTable} per table).";
        }
        catch
        {
            try
            {
                transaction.Rollback();
                progress.Report(new(0, TotalRecordOperations, "Rolling back", "An error occurred; all partial demo changes were rolled back.", "ROLLBACK OK | No partial demo data was retained.", 0, IsError: true));
            }
            catch (Exception rollbackException)
            {
                progress.Report(new(0, TotalRecordOperations, "Rollback failed", rollbackException.Message, $"ROLLBACK ERROR | {rollbackException.Message}", 0, IsError: true));
            }

            throw;
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<DemoTableDefinition> CreateDefinitions()
    {
        return new List<DemoTableDefinition>
        {
            new(
                "DemoSites",
                "CREATE TABLE IF NOT EXISTS DemoSites(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,BaseUrl TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,LastSyncAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoSites WHERE IsDemo=1;",
                "INSERT INTO DemoSites(Name,BaseUrl,Status,SeoScore,LastSyncAtUtc,IsDemo) VALUES($name,$url,$status,$score,$now,1);",
                (command, i, now) =>
                {
                    command.Parameters.AddWithValue("$name", $"Demo WordPress Site {i:000}");
                    command.Parameters.AddWithValue("$url", $"https://site-{i:000}.demo.local");
                    command.Parameters.AddWithValue("$status", i % 7 == 0 ? "NeedsReview" : "Connected");
                    command.Parameters.AddWithValue("$score", 55 + i % 46);
                    command.Parameters.AddWithValue("$now", now);
                }),
            new(
                "DemoPosts",
                "CREATE TABLE IF NOT EXISTS DemoPosts(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteName TEXT NOT NULL,Title TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,PublishedAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoPosts WHERE IsDemo=1;",
                "INSERT INTO DemoPosts(SiteName,Title,Status,SeoScore,PublishedAtUtc,IsDemo) VALUES($site,$title,$status,$score,$published,1);",
                (command, i, now) =>
                {
                    command.Parameters.AddWithValue("$site", $"Demo WordPress Site {((i - 1) % 100) + 1:000}");
                    command.Parameters.AddWithValue("$title", $"Demo Article {i:000}: SEO and WordPress Management");
                    command.Parameters.AddWithValue("$status", i % 3 == 0 ? "Draft" : i % 5 == 0 ? "NeedsReview" : "Published");
                    command.Parameters.AddWithValue("$score", 50 + i % 51);
                    command.Parameters.AddWithValue("$published", i % 3 == 0 ? DBNull.Value : now);
                }),
            new(
                "DemoCategories",
                "CREATE TABLE IF NOT EXISTS DemoCategories(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Slug TEXT NOT NULL,PostCount INTEGER NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoCategories WHERE IsDemo=1;",
                "INSERT INTO DemoCategories(Name,Slug,PostCount,IsDemo) VALUES($name,$slug,$count,1);",
                (command, i, _) =>
                {
                    command.Parameters.AddWithValue("$name", $"Demo Category {i:000}");
                    command.Parameters.AddWithValue("$slug", $"demo-category-{i:000}");
                    command.Parameters.AddWithValue("$count", i % 25);
                }),
            new(
                "DemoMedia",
                "CREATE TABLE IF NOT EXISTS DemoMedia(Id INTEGER PRIMARY KEY AUTOINCREMENT,FileName TEXT NOT NULL,MediaType TEXT NOT NULL,SizeBytes INTEGER NOT NULL,AltText TEXT NULL,OptimizationState TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoMedia WHERE IsDemo=1;",
                "INSERT INTO DemoMedia(FileName,MediaType,SizeBytes,AltText,OptimizationState,IsDemo) VALUES($file,$type,$size,$alt,$state,1);",
                (command, i, _) =>
                {
                    command.Parameters.AddWithValue("$file", $"demo-image-{i:000}.jpg");
                    command.Parameters.AddWithValue("$type", "image/jpeg");
                    command.Parameters.AddWithValue("$size", 120000 + i * 8192);
                    command.Parameters.AddWithValue("$alt", i % 6 == 0 ? DBNull.Value : $"Demo image alternative text {i:000}");
                    command.Parameters.AddWithValue("$state", i % 4 == 0 ? "NeedsOptimization" : "Optimized");
                }),
            new(
                "DemoTags",
                "CREATE TABLE IF NOT EXISTS DemoTags(Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Slug TEXT NOT NULL,UsageCount INTEGER NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoTags WHERE IsDemo=1;",
                "INSERT INTO DemoTags(Name,Slug,UsageCount,IsDemo) VALUES($name,$slug,$count,1);",
                (command, i, _) =>
                {
                    command.Parameters.AddWithValue("$name", $"Demo Tag {i:000}");
                    command.Parameters.AddWithValue("$slug", $"demo-tag-{i:000}");
                    command.Parameters.AddWithValue("$count", i % 40);
                }),
            new(
                "DemoJobs",
                "CREATE TABLE IF NOT EXISTS DemoJobs(Id INTEGER PRIMARY KEY AUTOINCREMENT,JobName TEXT NOT NULL,State TEXT NOT NULL,Progress INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoJobs WHERE IsDemo=1;",
                "INSERT INTO DemoJobs(JobName,State,Progress,CreatedAtUtc,IsDemo) VALUES($name,$state,$progress,$now,1);",
                (command, i, now) =>
                {
                    command.Parameters.AddWithValue("$name", $"Demo Background Job {i:000}");
                    command.Parameters.AddWithValue("$state", i % 8 == 0 ? "Failed" : i % 3 == 0 ? "Running" : "Completed");
                    command.Parameters.AddWithValue("$progress", i % 3 == 0 ? i % 100 : 100);
                    command.Parameters.AddWithValue("$now", now);
                }),
            new(
                "DemoNotifications",
                "CREATE TABLE IF NOT EXISTS DemoNotifications(Id INTEGER PRIMARY KEY AUTOINCREMENT,Severity TEXT NOT NULL,Title TEXT NOT NULL,Message TEXT NOT NULL,IsRead INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoNotifications WHERE IsDemo=1;",
                "INSERT INTO DemoNotifications(Severity,Title,Message,IsRead,CreatedAtUtc,IsDemo) VALUES($severity,$title,$message,$read,$now,1);",
                (command, i, now) =>
                {
                    command.Parameters.AddWithValue("$severity", i % 10 == 0 ? "Error" : i % 4 == 0 ? "Warning" : "Info");
                    command.Parameters.AddWithValue("$title", $"Demo Notification {i:000}");
                    command.Parameters.AddWithValue("$message", $"Generated notification for testing row {i:000}.");
                    command.Parameters.AddWithValue("$read", i % 3 == 0 ? 1 : 0);
                    command.Parameters.AddWithValue("$now", now);
                }),
            new(
                "DemoOperations",
                "CREATE TABLE IF NOT EXISTS DemoOperations(Id INTEGER PRIMARY KEY AUTOINCREMENT,Module TEXT NOT NULL,ActionName TEXT NOT NULL,State TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,Details TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoOperations WHERE IsDemo=1;",
                "INSERT INTO DemoOperations(Module,ActionName,State,CreatedAtUtc,Details,IsDemo) VALUES($module,$action,$state,$now,$details,1);",
                (command, i, now) =>
                {
                    var modules = new[] { "SEO", "Media", "Links", "Content", "Backup" };
                    command.Parameters.AddWithValue("$module", modules[(i - 1) % modules.Length]);
                    command.Parameters.AddWithValue("$action", $"Demo operation {i:000}");
                    command.Parameters.AddWithValue("$state", i % 5 == 0 ? "Pending" : i % 7 == 0 ? "Failed" : "Completed");
                    command.Parameters.AddWithValue("$now", now);
                    command.Parameters.AddWithValue("$details", $"Execution evidence for demo operation {i:000}.");
                }),
            new(
                "DemoSeoAudits",
                "CREATE TABLE IF NOT EXISTS DemoSeoAudits(Id INTEGER PRIMARY KEY AUTOINCREMENT,PageTitle TEXT NOT NULL,SeoScore INTEGER NOT NULL,HighIssues INTEGER NOT NULL,MediumIssues INTEGER NOT NULL,LowIssues INTEGER NOT NULL,AuditedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoSeoAudits WHERE IsDemo=1;",
                "INSERT INTO DemoSeoAudits(PageTitle,SeoScore,HighIssues,MediumIssues,LowIssues,AuditedAtUtc,IsDemo) VALUES($title,$score,$high,$medium,$low,$now,1);",
                (command, i, now) =>
                {
                    command.Parameters.AddWithValue("$title", $"Demo SEO Page {i:000}");
                    command.Parameters.AddWithValue("$score", 40 + i % 61);
                    command.Parameters.AddWithValue("$high", i % 6);
                    command.Parameters.AddWithValue("$medium", i % 10);
                    command.Parameters.AddWithValue("$low", i % 15);
                    command.Parameters.AddWithValue("$now", now);
                }),
            new(
                "DemoSuggestions",
                "CREATE TABLE IF NOT EXISTS DemoSuggestions(Id INTEGER PRIMARY KEY AUTOINCREMENT,SuggestionType TEXT NOT NULL,Title TEXT NOT NULL,Confidence INTEGER NOT NULL,RiskLevel TEXT NOT NULL,State TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
                "DELETE FROM DemoSuggestions WHERE IsDemo=1;",
                "INSERT INTO DemoSuggestions(SuggestionType,Title,Confidence,RiskLevel,State,CreatedAtUtc,IsDemo) VALUES($type,$title,$confidence,$risk,$state,$now,1);",
                (command, i, now) =>
                {
                    var types = new[] { "SetTitle", "SetExcerpt", "SetAltText", "AddInternalLink", "OptimizeCategory" };
                    command.Parameters.AddWithValue("$type", types[(i - 1) % types.Length]);
                    command.Parameters.AddWithValue("$title", $"Demo AI suggestion {i:000}");
                    command.Parameters.AddWithValue("$confidence", 60 + i % 41);
                    command.Parameters.AddWithValue("$risk", i % 9 == 0 ? "High" : i % 4 == 0 ? "Medium" : "Low");
                    command.Parameters.AddWithValue("$state", i % 5 == 0 ? "PendingReview" : "Approved");
                    command.Parameters.AddWithValue("$now", now);
                })
        };
    }
}
