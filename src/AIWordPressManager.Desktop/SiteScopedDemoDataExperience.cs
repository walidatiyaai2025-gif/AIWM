using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace AIWordPressManager.Desktop;

internal static class SiteScopedDemoDataExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            true);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: "DemoDataLauncher" } ||
            Window.GetWindow((DependencyObject)sender) is not MainWindow window)
        {
            return;
        }

        e.Handled = true;

        if (window.DataContext is not MainWindowViewModel main || main.Sites.SelectedSite is not SiteCardViewModel site)
        {
            MessageBox.Show(
                window,
                "Select a WordPress site first. Demo data is always created for the active site.",
                "Demo Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (window.Tag is not string databasePath || string.IsNullOrWhiteSpace(databasePath))
        {
            MessageBox.Show(window, "The SQLite database path is not available.", "Demo Data", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        new SiteScopedDemoDataWindow(databasePath, site.Id, site.Name, site.SiteUrl)
        {
            Owner = window
        }.ShowDialog();
    }
}

internal sealed class SiteScopedDemoDataWindow : Window
{
    private readonly string _databasePath;
    private readonly Guid _siteId;
    private readonly string _siteName;
    private readonly string _siteUrl;
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 24 };
    private readonly TextBlock _stage = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _counter = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _elapsed = new();
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontFamily = new FontFamily("Consolas"),
        MinHeight = 280
    };
    private readonly Button _refresh = new() { Content = "Refresh current site demo data", Padding = new Thickness(16, 9, 16, 9), IsDefault = true };
    private readonly Button _copy = new() { Content = "Copy Log", Padding = new Thickness(12, 8, 12, 8) };
    private readonly Button _save = new() { Content = "Save Log", Padding = new Thickness(12, 8, 12, 8) };
    private readonly Button _close = new() { Content = "Close", Padding = new Thickness(16, 9, 16, 9), IsCancel = true };
    private readonly Stopwatch _watch = new();

    internal SiteScopedDemoDataWindow(string databasePath, Guid siteId, string siteName, string siteUrl)
    {
        _databasePath = databasePath;
        _siteId = siteId;
        _siteName = siteName;
        _siteUrl = siteUrl;

        Title = $"Demo Data — {siteName}";
        Width = 900;
        Height = 740;
        MinWidth = 760;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(24) };
        for (var i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Site-scoped Demo Data", FontSize = 26, FontWeight = FontWeights.Bold });

        var siteInfo = new TextBlock
        {
            Text = $"Active site: {_siteName}\nURL: {_siteUrl}\nSite ID: {_siteId:D}",
            Margin = new Thickness(0, 8, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(siteInfo, 1);
        root.Children.Add(siteInfo);

        Grid.SetRow(_stage, 2); root.Children.Add(_stage);
        _details.Margin = new Thickness(0, 5, 0, 10); Grid.SetRow(_details, 3); root.Children.Add(_details);
        Grid.SetRow(_progress, 4); root.Children.Add(_progress);

        var counters = new Grid { Margin = new Thickness(0, 8, 0, 10) };
        counters.ColumnDefinitions.Add(new ColumnDefinition());
        counters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        counters.Children.Add(_counter);
        Grid.SetColumn(_elapsed, 1); counters.Children.Add(_elapsed);
        Grid.SetRow(counters, 5); root.Children.Add(counters);

        Grid.SetRow(_log, 6); root.Children.Add(_log);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        _copy.Margin = new Thickness(0, 0, 8, 0);
        _save.Margin = new Thickness(0, 0, 8, 0);
        _refresh.Margin = new Thickness(0, 0, 8, 0);
        _copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_log.Text)) Clipboard.SetText(_log.Text); };
        _save.Click += (_, _) => SaveLog();
        _refresh.Click += async (_, _) => await RunAsync();
        actions.Children.Add(_copy);
        actions.Children.Add(_save);
        actions.Children.Add(_refresh);
        actions.Children.Add(_close);
        Grid.SetRow(actions, 7); root.Children.Add(actions);

        Content = root;
        SetState(0, "Ready", "Only the active site's demo rows will be replaced.", 0);
    }

    private async Task RunAsync()
    {
        _refresh.IsEnabled = false;
        _close.IsEnabled = false;
        _log.Clear();
        _watch.Restart();

        var progress = new Progress<SiteDemoProgress>(p =>
        {
            SetState(p.Percent, p.Stage, p.Details, p.Completed);
            Append($"[{DateTime.Now:HH:mm:ss}] {p.LogLine}");
        });

        try
        {
            Append($"[{DateTime.Now:HH:mm:ss}] START | Site={_siteName} | SiteId={_siteId:D}");
            var summary = await SiteScopedDemoSeeder.RefreshAsync(_databasePath, _siteId, _siteName, _siteUrl, progress);
            SetState(100, "Completed successfully", summary, SiteScopedDemoSeeder.TotalRecords);
            Append($"[{DateTime.Now:HH:mm:ss}] SUCCESS | {summary}");
        }
        catch (Exception ex)
        {
            SetState(_progress.Value, "Failed", ex.Message, ParseCount());
            Append($"[{DateTime.Now:HH:mm:ss}] ERROR | {ex}");
            MessageBox.Show(this, ex.ToString(), "Demo Data Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsed.Text = $"Elapsed: {_watch.Elapsed:hh\\:mm\\:ss}";
            _refresh.IsEnabled = true;
            _close.IsEnabled = true;
        }
    }

    private void SetState(double percent, string stage, string details, int completed)
    {
        _progress.Value = percent;
        _stage.Text = $"{percent:N1}% — {stage}";
        _details.Text = details;
        _counter.Text = $"Current site records: {completed} / {SiteScopedDemoSeeder.TotalRecords}";
        _elapsed.Text = $"Elapsed: {_watch.Elapsed:hh\\:mm\\:ss}";
    }

    private int ParseCount()
    {
        var digits = new string(_counter.Text.SkipWhile(x => !char.IsDigit(x)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var count) ? count : 0;
    }

    private void Append(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private void SaveLog()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt",
            FileName = $"demo-{Sanitize(_siteName)}-{DateTime.Now:yyyyMMdd-HHmmss}.log"
        };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, _log.Text, Encoding.UTF8);
    }

    private static string Sanitize(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

internal sealed record SiteDemoProgress(int Completed, int Total, string Stage, string Details, string LogLine)
{
    internal double Percent => Total == 0 ? 0 : Math.Clamp(Completed * 100d / Total, 0, 100);
}

internal static class SiteScopedDemoSeeder
{
    internal const int RecordsPerTable = 100;
    internal const int TableCount = 10;
    internal const int TotalRecords = RecordsPerTable * TableCount;

    private sealed record Definition(string Name, string CreateSql, string InsertSql, Action<SqliteCommand, int, string, string, string> Bind);

    internal static async Task<string> RefreshAsync(
        string databasePath,
        Guid siteId,
        string siteName,
        string siteUrl,
        IProgress<SiteDemoProgress> progress,
        CancellationToken cancellationToken = default)
    {
        await UserSecurityStore.EnsureCreatedAsync(databasePath);
        await using var connection = new SqliteConnection($"Data Source={databasePath};Default Timeout=30");
        await connection.OpenAsync(cancellationToken);

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000; PRAGMA foreign_keys=ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        var definitions = Definitions();
        var site = siteId.ToString("D", CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var definition in definitions)
            {
                await ExecuteAsync(connection, transaction, definition.CreateSql, cancellationToken);
                await EnsureSiteIdColumnAsync(connection, transaction, definition.Name, cancellationToken);

                await using var delete = connection.CreateCommand();
                delete.Transaction = transaction;
                delete.CommandText = $"DELETE FROM \"{definition.Name}\" WHERE IsDemo=1 AND SiteId=$siteId;";
                delete.Parameters.AddWithValue("$siteId", site);
                var removed = await delete.ExecuteNonQueryAsync(cancellationToken);
                progress.Report(new(0, TotalRecords, "Preparing schema", $"{definition.Name} ready for {_siteName(siteName)}.", $"TABLE READY | {definition.Name} | old rows removed={removed}"));
            }

            var completed = 0;
            foreach (var definition in definitions)
            {
                for (var index = 1; index <= RecordsPerTable; index++)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = definition.InsertSql;
                    command.Parameters.AddWithValue("$siteId", site);
                    definition.Bind(command, index, now, siteName, siteUrl);
                    var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                    if (affected != 1) throw new InvalidOperationException($"{definition.Name} row {index} was not inserted.");
                    completed++;
                    progress.Report(new(completed, TotalRecords, $"Populating {definition.Name}", $"Inserted {index}/{RecordsPerTable} for {siteName}.", $"INSERT OK | {definition.Name} | SiteId={site} | row={index}/{RecordsPerTable}"));
                    if (index % 10 == 0) await Task.Yield();
                }
            }

            await using (var audit = connection.CreateCommand())
            {
                audit.Transaction = transaction;
                audit.CommandText = """
                    CREATE TABLE IF NOT EXISTS DemoSeedRuns(
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        SiteId TEXT NULL,
                        SeedVersion TEXT NOT NULL,
                        SeededAtUtc TEXT NOT NULL,
                        Summary TEXT NOT NULL);
                    INSERT INTO DemoSeedRuns(SiteId,SeedVersion,SeededAtUtc,Summary)
                    VALUES($siteId,'4.0-site-scoped',$now,$summary);
                    """;
                audit.Parameters.AddWithValue("$siteId", site);
                audit.Parameters.AddWithValue("$now", now);
                audit.Parameters.AddWithValue("$summary", $"{TotalRecords} demo rows for {siteName}.");
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            progress.Report(new(TotalRecords, TotalRecords, "Committing transaction", "All site-scoped inserts succeeded.", $"COMMIT START | SiteId={site}"));
            transaction.Commit();
            progress.Report(new(TotalRecords, TotalRecords, "Commit completed", "SQLite saved all rows for the active site.", $"COMMIT OK | SiteId={site}"));
            return $"Created {TotalRecords} demo rows for {siteName} only.";
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private static string _siteName(string value) => string.IsNullOrWhiteSpace(value) ? "the selected site" : value;

    private static async Task EnsureSiteIdColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string table, CancellationToken token)
    {
        var exists = false;
        await using (var info = connection.CreateCommand())
        {
            info.Transaction = transaction;
            info.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var reader = await info.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (string.Equals(reader.GetString(1), "SiteId", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            }
        }
        if (!exists) await ExecuteAsync(connection, transaction, $"ALTER TABLE \"{table}\" ADD COLUMN SiteId TEXT NULL;", token);
        await ExecuteAsync(connection, transaction, $"CREATE INDEX IF NOT EXISTS \"IX_{table}_SiteId\" ON \"{table}\"(SiteId);", token);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(token);
    }

    private static IReadOnlyList<Definition> Definitions() =>
    [
        new("DemoSites",
            "CREATE TABLE IF NOT EXISTS DemoSites(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,Name TEXT NOT NULL,BaseUrl TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,LastSyncAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoSites(SiteId,Name,BaseUrl,Status,SeoScore,LastSyncAtUtc,IsDemo) VALUES($siteId,$name,$url,$status,$score,$now,1);",
            (c,i,n,s,u) => { c.Parameters.AddWithValue("$name",$"{s} Demo Workspace {i:000}"); c.Parameters.AddWithValue("$url",u); c.Parameters.AddWithValue("$status",i%7==0?"NeedsReview":"Connected"); c.Parameters.AddWithValue("$score",55+i%46); c.Parameters.AddWithValue("$now",n); }),
        new("DemoPosts",
            "CREATE TABLE IF NOT EXISTS DemoPosts(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,SiteName TEXT NOT NULL,Title TEXT NOT NULL,Status TEXT NOT NULL,SeoScore INTEGER NOT NULL,PublishedAtUtc TEXT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoPosts(SiteId,SiteName,Title,Status,SeoScore,PublishedAtUtc,IsDemo) VALUES($siteId,$site,$title,$status,$score,$published,1);",
            (c,i,n,s,_) => { c.Parameters.AddWithValue("$site",s); c.Parameters.AddWithValue("$title",$"{s} Demo Article {i:000}"); c.Parameters.AddWithValue("$status",i%3==0?"Draft":i%5==0?"NeedsReview":"Published"); c.Parameters.AddWithValue("$score",50+i%51); c.Parameters.AddWithValue("$published",i%3==0?DBNull.Value:n); }),
        new("DemoCategories",
            "CREATE TABLE IF NOT EXISTS DemoCategories(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,Name TEXT NOT NULL,Slug TEXT NOT NULL,PostCount INTEGER NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoCategories(SiteId,Name,Slug,PostCount,IsDemo) VALUES($siteId,$name,$slug,$count,1);",
            (c,i,_,s,_) => { c.Parameters.AddWithValue("$name",$"{s} Category {i:000}"); c.Parameters.AddWithValue("$slug",$"demo-category-{i:000}"); c.Parameters.AddWithValue("$count",i%25); }),
        new("DemoMedia",
            "CREATE TABLE IF NOT EXISTS DemoMedia(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,FileName TEXT NOT NULL,MediaType TEXT NOT NULL,SizeBytes INTEGER NOT NULL,AltText TEXT NULL,OptimizationState TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoMedia(SiteId,FileName,MediaType,SizeBytes,AltText,OptimizationState,IsDemo) VALUES($siteId,$file,$type,$size,$alt,$state,1);",
            (c,i,_,s,_) => { c.Parameters.AddWithValue("$file",$"{s.Replace(" ","-").ToLowerInvariant()}-{i:000}.jpg"); c.Parameters.AddWithValue("$type","image/jpeg"); c.Parameters.AddWithValue("$size",120000+i*8192); c.Parameters.AddWithValue("$alt",i%6==0?DBNull.Value:$"{s} image {i:000}"); c.Parameters.AddWithValue("$state",i%4==0?"NeedsOptimization":"Optimized"); }),
        new("DemoTags",
            "CREATE TABLE IF NOT EXISTS DemoTags(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,Name TEXT NOT NULL,Slug TEXT NOT NULL,UsageCount INTEGER NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoTags(SiteId,Name,Slug,UsageCount,IsDemo) VALUES($siteId,$name,$slug,$count,1);",
            (c,i,_,s,_) => { c.Parameters.AddWithValue("$name",$"{s} Tag {i:000}"); c.Parameters.AddWithValue("$slug",$"demo-tag-{i:000}"); c.Parameters.AddWithValue("$count",i%40); }),
        new("DemoJobs",
            "CREATE TABLE IF NOT EXISTS DemoJobs(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,JobName TEXT NOT NULL,State TEXT NOT NULL,Progress INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoJobs(SiteId,JobName,State,Progress,CreatedAtUtc,IsDemo) VALUES($siteId,$name,$state,$progress,$now,1);",
            (c,i,n,s,_) => { c.Parameters.AddWithValue("$name",$"{s} Job {i:000}"); c.Parameters.AddWithValue("$state",i%8==0?"Failed":i%3==0?"Running":"Completed"); c.Parameters.AddWithValue("$progress",i%3==0?i%100:100); c.Parameters.AddWithValue("$now",n); }),
        new("DemoNotifications",
            "CREATE TABLE IF NOT EXISTS DemoNotifications(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,Severity TEXT NOT NULL,Title TEXT NOT NULL,Message TEXT NOT NULL,IsRead INTEGER NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoNotifications(SiteId,Severity,Title,Message,IsRead,CreatedAtUtc,IsDemo) VALUES($siteId,$severity,$title,$message,$read,$now,1);",
            (c,i,n,s,_) => { c.Parameters.AddWithValue("$severity",i%10==0?"Error":i%4==0?"Warning":"Info"); c.Parameters.AddWithValue("$title",$"{s} Notification {i:000}"); c.Parameters.AddWithValue("$message",$"Demo notification for {s}, row {i:000}."); c.Parameters.AddWithValue("$read",i%3==0?1:0); c.Parameters.AddWithValue("$now",n); }),
        new("DemoOperations",
            "CREATE TABLE IF NOT EXISTS DemoOperations(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,Module TEXT NOT NULL,ActionName TEXT NOT NULL,State TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,Details TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoOperations(SiteId,Module,ActionName,State,CreatedAtUtc,Details,IsDemo) VALUES($siteId,$module,$action,$state,$now,$details,1);",
            (c,i,n,s,_) => { var m=new[]{"SEO","Media","Links","Content","Backup"}; c.Parameters.AddWithValue("$module",m[(i-1)%m.Length]); c.Parameters.AddWithValue("$action",$"{s} operation {i:000}"); c.Parameters.AddWithValue("$state",i%5==0?"Pending":i%7==0?"Failed":"Completed"); c.Parameters.AddWithValue("$now",n); c.Parameters.AddWithValue("$details",$"Evidence for {s} operation {i:000}."); }),
        new("DemoSeoAudits",
            "CREATE TABLE IF NOT EXISTS DemoSeoAudits(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,PageTitle TEXT NOT NULL,SeoScore INTEGER NOT NULL,HighIssues INTEGER NOT NULL,MediumIssues INTEGER NOT NULL,LowIssues INTEGER NOT NULL,AuditedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoSeoAudits(SiteId,PageTitle,SeoScore,HighIssues,MediumIssues,LowIssues,AuditedAtUtc,IsDemo) VALUES($siteId,$title,$score,$high,$medium,$low,$now,1);",
            (c,i,n,s,_) => { c.Parameters.AddWithValue("$title",$"{s} SEO Page {i:000}"); c.Parameters.AddWithValue("$score",40+i%61); c.Parameters.AddWithValue("$high",i%6); c.Parameters.AddWithValue("$medium",i%10); c.Parameters.AddWithValue("$low",i%15); c.Parameters.AddWithValue("$now",n); }),
        new("DemoSuggestions",
            "CREATE TABLE IF NOT EXISTS DemoSuggestions(Id INTEGER PRIMARY KEY AUTOINCREMENT,SiteId TEXT NULL,SuggestionType TEXT NOT NULL,Title TEXT NOT NULL,Confidence INTEGER NOT NULL,RiskLevel TEXT NOT NULL,State TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,IsDemo INTEGER NOT NULL DEFAULT 1);",
            "INSERT INTO DemoSuggestions(SiteId,SuggestionType,Title,Confidence,RiskLevel,State,CreatedAtUtc,IsDemo) VALUES($siteId,$type,$title,$confidence,$risk,$state,$now,1);",
            (c,i,n,s,_) => { var t=new[]{"SetTitle","SetExcerpt","SetAltText","AddInternalLink","OptimizeCategory"}; c.Parameters.AddWithValue("$type",t[(i-1)%t.Length]); c.Parameters.AddWithValue("$title",$"{s} AI suggestion {i:000}"); c.Parameters.AddWithValue("$confidence",60+i%41); c.Parameters.AddWithValue("$risk",i%9==0?"High":i%4==0?"Medium":"Low"); c.Parameters.AddWithValue("$state",i%5==0?"PendingReview":"Approved"); c.Parameters.AddWithValue("$now",n); })
    ];
}
