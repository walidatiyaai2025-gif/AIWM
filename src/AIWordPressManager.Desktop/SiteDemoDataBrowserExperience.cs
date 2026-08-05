using System.Collections.ObjectModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal static class SiteDemoDataBrowserExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> AttachedWindows = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (AttachedWindows.TryGetValue(window, out _))
            return;

        AttachedWindows.Add(window, new object());
        window.Dispatcher.BeginInvoke(() => InstallButton(window));
    }

    private static void InstallButton(MainWindow window)
    {
        if (window.Content is not Grid root ||
            root.Children.OfType<Button>().Any(button => Equals(button.Tag, "DemoDataBrowserLauncher")))
        {
            return;
        }

        var button = new Button
        {
            Tag = "DemoDataBrowserLauncher",
            Content = "📊  Demo Records",
            ToolTip = "View the demo rows physically stored in SQLite for the active site",
            Padding = new Thickness(13, 7, 13, 7),
            Margin = new Thickness(0, 5, 245, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };
        Panel.SetZIndex(button, 5001);

        button.Click += async (_, _) =>
        {
            if (window.DataContext is not MainWindowViewModel main ||
                main.Sites.SelectedSite is not SiteCardViewModel site)
            {
                MessageBox.Show(
                    window,
                    "Select a WordPress site first.",
                    "Demo Records",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (window.Tag is not string databasePath || string.IsNullOrWhiteSpace(databasePath))
            {
                MessageBox.Show(
                    window,
                    "The active SQLite database path is not available.",
                    "Demo Records",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var browser = new SiteDemoDataBrowserWindow(databasePath, site)
            {
                Owner = window
            };
            await browser.LoadAsync();
            browser.ShowDialog();
        };

        Grid.SetRow(button, 0);
        Grid.SetColumnSpan(button, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(button);
    }
}

internal sealed class SiteDemoDataBrowserWindow : Window
{
    private static readonly string[] Tables =
    [
        "DemoSites",
        "DemoPosts",
        "DemoCategories",
        "DemoMedia",
        "DemoTags",
        "DemoJobs",
        "DemoNotifications",
        "DemoOperations",
        "DemoSeoAudits",
        "DemoSuggestions"
    ];

    private readonly string _databasePath;
    private readonly SiteCardViewModel _site;
    private readonly ObservableCollection<DemoTableCountRow> _counts = [];
    private readonly ComboBox _tableSelector = new() { MinWidth = 210 };
    private readonly DataGrid _countGrid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        MinHeight = 210
    };
    private readonly DataGrid _recordsGrid = new()
    {
        IsReadOnly = true,
        AutoGenerateColumns = true,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        MinHeight = 300
    };
    private readonly TextBlock _summary = new()
    {
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.75
    };
    private readonly Button _refresh = new()
    {
        Content = "Refresh from SQLite",
        Padding = new Thickness(14, 8, 14, 8)
    };

    internal SiteDemoDataBrowserWindow(string databasePath, SiteCardViewModel site)
    {
        _databasePath = databasePath;
        _site = site;

        Title = $"Demo Records — {site.Name}";
        Width = 1080;
        Height = 760;
        MinWidth = 880;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _countGrid.Columns.Add(new DataGridTextColumn { Header = "Table", Binding = new System.Windows.Data.Binding(nameof(DemoTableCountRow.TableName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _countGrid.Columns.Add(new DataGridTextColumn { Header = "Rows", Binding = new System.Windows.Data.Binding(nameof(DemoTableCountRow.RowCount)), Width = 100 });
        _countGrid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new System.Windows.Data.Binding(nameof(DemoTableCountRow.Status)), Width = 140 });
        _countGrid.ItemsSource = _counts;

        _tableSelector.ItemsSource = Tables;
        _tableSelector.SelectedIndex = 0;
        _tableSelector.SelectionChanged += async (_, _) => await LoadSelectedTableAsync();
        _refresh.Click += async (_, _) => await LoadAsync();

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Site-scoped Demo Data Browser",
            FontSize = 25,
            FontWeight = FontWeights.Bold
        });

        var siteInfo = new TextBlock
        {
            Text = $"Active site: {_site.Name}\nURL: {_site.SiteUrl}\nSite ID: {_site.Id:D}",
            Margin = new Thickness(0, 7, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(siteInfo, 1);
        root.Children.Add(siteInfo);

        Grid.SetRow(_summary, 2);
        root.Children.Add(_summary);

        _status.Margin = new Thickness(0, 4, 0, 10);
        Grid.SetRow(_status, 3);
        root.Children.Add(_status);

        Grid.SetRow(_countGrid, 4);
        root.Children.Add(_countGrid);

        var selectorPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 8)
        };
        selectorPanel.Children.Add(new TextBlock
        {
            Text = "Preview table:",
            Margin = new Thickness(0, 7, 10, 0),
            FontWeight = FontWeights.SemiBold
        });
        selectorPanel.Children.Add(_tableSelector);
        Grid.SetRow(selectorPanel, 5);
        root.Children.Add(selectorPanel);

        Grid.SetRow(_recordsGrid, 6);
        root.Children.Add(_recordsGrid);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _refresh.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_refresh);
        actions.Children.Add(new Button
        {
            Content = "Close",
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true
        });
        Grid.SetRow(actions, 7);
        root.Children.Add(actions);

        Content = root;
    }

    internal async Task LoadAsync()
    {
        _refresh.IsEnabled = false;
        _status.Text = "Reading physically stored rows from SQLite…";
        _counts.Clear();

        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Default Timeout=30");
            await connection.OpenAsync();

            var total = 0;
            foreach (var table in Tables)
            {
                var count = await CountRowsAsync(connection, table, _site.Id);
                total += count;
                _counts.Add(new DemoTableCountRow(table, count, count >= 100 ? "Ready" : count == 0 ? "Empty" : "Incomplete"));
            }

            _summary.Text = $"Verified rows for {_site.Name}: {total:N0} across {Tables.Length} tables.";
            _status.Text = total >= Tables.Length * 100
                ? "All required demo records are physically stored for the active site."
                : "Some tables contain fewer than 100 rows. Run Demo Data for this site, then refresh.";

            await LoadSelectedTableAsync(connection);
        }
        catch (Exception exception)
        {
            _status.Text = "Could not read demo records. " + GetInnermostMessage(exception);
        }
        finally
        {
            _refresh.IsEnabled = true;
        }
    }

    private async Task LoadSelectedTableAsync()
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Default Timeout=30");
            await connection.OpenAsync();
            await LoadSelectedTableAsync(connection);
        }
        catch (Exception exception)
        {
            _status.Text = "Could not load table preview. " + GetInnermostMessage(exception);
        }
    }

    private async Task LoadSelectedTableAsync(SqliteConnection connection)
    {
        var table = _tableSelector.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(table) || !Tables.Contains(table, StringComparer.Ordinal))
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\" WHERE IsDemo=1 AND SiteId=$siteId ORDER BY Id DESC LIMIT 100;";
        command.Parameters.AddWithValue("$siteId", _site.Id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync();
        var data = new DataTable(table);
        data.Load(reader);
        _recordsGrid.ItemsSource = data.DefaultView;
    }

    private static async Task<int> CountRowsAsync(SqliteConnection connection, string table, Guid siteId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE IsDemo=1 AND SiteId=$siteId;";
        command.Parameters.AddWithValue("$siteId", siteId.ToString("D"));
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetInnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }
}

internal sealed record DemoTableCountRow(string TableName, int RowCount, string Status);
