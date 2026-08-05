using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

namespace AIWordPressManager.Desktop;

internal static class SystemTimerSetupExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded), true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window) || Attached.TryGetValue(window, out _))
            return;

        Attached.Add(window, new object());
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => InstallSystemButton(window)));
    }

    private static void InstallSystemButton(MainWindow window)
    {
        var systemTab = FindVisualChildren<TabItem>(window)
            .FirstOrDefault(x => string.Equals(x.Header?.ToString(), "SYSTEM", StringComparison.OrdinalIgnoreCase));
        if (systemTab?.Content is not Panel panel || panel.Children.OfType<Button>().Any(x => Equals(x.Tag, "TimerSetup")))
            return;

        var button = new Button
        {
            Tag = "TimerSetup",
            Content = "⏱\nTimer Setup",
            ToolTip = "Configure synchronization, refresh, monitoring, notification, and other application timers.",
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 95
        };
        button.Click += async (_, _) =>
        {
            var path = window.Tag as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(window, "The active SQLite database path is unavailable.", "Timer Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var timerWindow = new SystemTimerSetupWindow(path) { Owner = window };
            await timerWindow.LoadAsync();
            timerWindow.ShowDialog();
        };
        panel.Children.Add(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is not System.Windows.Media.Visual && parent is not System.Windows.Media.Media3D.Visual3D)
            yield break;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
}

internal sealed class SystemTimerSetupWindow : Window
{
    private readonly string _databasePath;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<TimerSettingRow> _items = new();

    internal SystemTimerSetupWindow(string databasePath)
    {
        _databasePath = databasePath;
        Title = "System Timer Setup";
        Width = 820;
        Height = 650;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "System Timer Setup", FontSize = 26, FontWeight = FontWeights.Bold });
        var description = new TextBlock
        {
            Text = "Every discovered application DispatcherTimer is listed here. Changes are saved in SQLite and applied immediately.",
            Margin = new Thickness(0, 8, 0, 16),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
        Grid.SetRow(description, 1);
        root.Children.Add(description);

        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_status);
        var save = new Button { Content = "Save and apply", Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(8, 0, 8, 0) };
        save.Click += async (_, _) => await SaveAsync();
        Grid.SetColumn(save, 1);
        footer.Children.Add(save);
        var close = new Button { Content = "Close", Padding = new Thickness(18, 9, 18, 9), IsCancel = true };
        Grid.SetColumn(close, 2);
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;
    }

    internal async Task LoadAsync()
    {
        await EnsureTableAsync();
        var discovered = DiscoverTimers();
        var stored = await LoadStoredAsync();
        _rows.Children.Clear();
        _items.Clear();

        foreach (var timer in discovered)
        {
            if (stored.TryGetValue(timer.Key, out var value))
            {
                timer.Enabled.IsChecked = value.Enabled;
                timer.Seconds.Text = value.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            _items.Add(timer);
            _rows.Children.Add(timer.Container);
        }

        _status.Text = $"Discovered {_items.Count} timer(s).";
    }

    private List<TimerSettingRow> DiscoverTimers()
    {
        var result = new List<TimerSettingRow>();
        var assembly = typeof(MainWindow).Assembly;
        foreach (var type in assembly.GetTypes().OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(DispatcherTimer).IsAssignableFrom(field.FieldType) || field.GetValue(null) is not DispatcherTimer timer)
                    continue;
                var key = $"{type.FullName}.{field.Name}";
                result.Add(new TimerSettingRow(key, type.Name + " / " + field.Name, timer));
            }
        }
        return result;
    }

    private async Task SaveAsync()
    {
        _status.Text = "Saving and applying timer settings…";
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        foreach (var item in _items)
        {
            if (!double.TryParse(item.Seconds.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                seconds = Math.Max(1, item.Timer.Interval.TotalSeconds);
            seconds = Math.Clamp(seconds, 1, 86400);
            var enabled = item.Enabled.IsChecked == true;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SystemTimerSettings(TimerKey,Enabled,IntervalSeconds,UpdatedAtUtc)
                VALUES($key,$enabled,$seconds,$updated)
                ON CONFLICT(TimerKey) DO UPDATE SET
                    Enabled=excluded.Enabled,
                    IntervalSeconds=excluded.IntervalSeconds,
                    UpdatedAtUtc=excluded.UpdatedAtUtc;
                """;
            command.Parameters.AddWithValue("$key", item.Key);
            command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
            command.Parameters.AddWithValue("$seconds", seconds);
            command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync();

            item.Timer.Interval = TimeSpan.FromSeconds(seconds);
            if (enabled) item.Timer.Start(); else item.Timer.Stop();
        }

        transaction.Commit();
        _status.Text = $"Saved and applied {_items.Count} timer setting(s).";
    }

    private async Task EnsureTableAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SystemTimerSettings(
                TimerKey TEXT PRIMARY KEY,
                Enabled INTEGER NOT NULL,
                IntervalSeconds REAL NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Dictionary<string, StoredTimerSetting>> LoadStoredAsync()
    {
        var values = new Dictionary<string, StoredTimerSetting>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TimerKey,Enabled,IntervalSeconds FROM SystemTimerSettings;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values[reader.GetString(0)] = new StoredTimerSetting(reader.GetInt64(1) == 1, reader.GetDouble(2));
        return values;
    }

    private sealed record StoredTimerSetting(bool Enabled, double IntervalSeconds);

    private sealed class TimerSettingRow
    {
        internal string Key { get; }
        internal DispatcherTimer Timer { get; }
        internal CheckBox Enabled { get; }
        internal TextBox Seconds { get; }
        internal Border Container { get; }

        internal TimerSettingRow(string key, string title, DispatcherTimer timer)
        {
            Key = key;
            Timer = timer;
            Enabled = new CheckBox { Content = "Enabled", IsChecked = timer.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
            Seconds = new TextBox
            {
                Text = Math.Max(1, timer.Interval.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Width = 100,
                Padding = new Thickness(8, 5, 8, 5)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold });
            text.Children.Add(new TextBlock { Text = key, Opacity = 0.6, FontSize = 10, TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(text);
            Grid.SetColumn(Enabled, 1);
            Enabled.Margin = new Thickness(12, 0, 12, 0);
            grid.Children.Add(Enabled);
            var interval = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            interval.Children.Add(new TextBlock { Text = "Seconds:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            interval.Children.Add(Seconds);
            Grid.SetColumn(interval, 2);
            grid.Children.Add(interval);
            Container = new Border { Child = grid, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        }
    }
}
