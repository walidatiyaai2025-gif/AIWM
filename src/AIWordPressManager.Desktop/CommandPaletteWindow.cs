using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIWordPressManager.Desktop;

public sealed class CommandPaletteWindow : Window
{
    public const string OptimizeMemoryCommand = "__OPTIMIZE_MEMORY__";
    public const string CleanMemoryCommand = "__CLEAN_MEMORY__";

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "Workspace", "command-palette.json");

    private readonly Func<string, Task> _execute;
    private readonly TextBox _search = new();
    private readonly ListBox _results = new();
    private readonly TextBlock _count = new();
    private readonly ObservableCollection<PaletteCommand> _visibleCommands = [];
    private readonly List<string> _recentDestinations = [];

    private readonly List<PaletteCommand> _commands =
    [
        new("Dashboard", "لوحة التحكم", "Dashboard", "Workspace", "⌂"),
        new("Sites", "المواقع", "Sites", "Workspace", "◉"),
        new("WordPress Explorer", "مستكشف ووردبريس", "WordPress Explorer", "Content", "▣"),
        new("Content Audit", "فحص المحتوى", "Content Audit", "Content", "✓"),
        new("SEO Audit", "فحص السيو", "SEO Audit", "SEO", "↗"),
        new("SEO History", "سجل السيو", "SEO History", "SEO", "⌁"),
        new("Post SEO Editor", "محرر سيو المقال", "Post SEO Editor", "SEO", "✎"),
        new("Suggested Changes", "التغييرات المقترحة", "Suggested Changes", "AI & Automation", "!"),
        new("Approval Queue", "قائمة الموافقات", "Approval Queue", "AI & Automation", "✓"),
        new("Execution Center", "مركز التنفيذ", "Execution Center", "AI & Automation", "▶"),
        new("AI Studio", "استوديو الذكاء الاصطناعي", "AI Studio", "AI & Automation", "✦"),
        new("Jobs", "المهام المجدولة", "Jobs", "System", "◷"),
        new("Notification Center", "مركز الإشعارات", "Notification Center", "System", "🔔"),
        new("Performance & Memory", "الأداء والذاكرة", "Performance", "System", "⚡"),
        new("Clean memory now", "تنظيف الذاكرة الآن", CleanMemoryCommand, "System", "⌫"),
        new("Release hidden grid caches", "تحرير ذاكرة الجداول المخفية", OptimizeMemoryCommand, "System", "▦"),
        new("Database Backup & Restore", "نسخ واستعادة قاعدة البيانات", "Backups", "System", "⟳"),
        new("Reports", "التقارير", "Reports", "System", "▥"),
        new("Logs", "السجلات", "Logs", "System", "≣"),
        new("Settings", "الإعدادات", "Settings", "System", "⚙"),
        new("Help & User Guide", "دليل المستخدم", "Help", "System", "?")
    ];

    public CommandPaletteWindow(Func<string, Task> execute)
    {
        _execute = execute;
        LoadState();

        Title = "Command Palette — لوحة الأوامر";
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock
        {
            Text = "Search screens and commands",
            FontSize = 20,
            FontWeight = FontWeights.Bold
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Type in Arabic or English. Use ↑ ↓ to navigate, Enter to open, Esc to close.",
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = .72
        });
        root.Children.Add(heading);

        _search.Height = 46;
        _search.FontSize = 15;
        _search.Padding = new Thickness(14, 10, 14, 10);
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.ToolTip = "Search by screen, category, command or destination";
        _search.TextChanged += (_, _) => Refresh();
        _search.KeyDown += SearchOnKeyDown;
        Grid.SetRow(_search, 1);
        root.Children.Add(_search);

        _results.Margin = new Thickness(0, 12, 0, 10);
        _results.ItemsSource = _visibleCommands;
        _results.MouseDoubleClick += async (_, _) => await ExecuteSelectedAsync();
        _results.KeyDown += ResultsOnKeyDown;
        _results.ItemTemplate = CreateItemTemplate();
        _results.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(_results, 2);
        root.Children.Add(_results);

        var footer = new DockPanel { LastChildFill = true };
        _count.Opacity = .68;
        _count.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_count, Dock.Left);
        footer.Children.Add(_count);
        footer.Children.Add(new TextBlock
        {
            Text = "Ctrl+Shift+P",
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = .62,
            FontWeight = FontWeights.SemiBold
        });
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        Loaded += (_, _) =>
        {
            Refresh();
            _search.Focus();
            Keyboard.Focus(_search);
        };
    }

    private static DataTemplate CreateItemTemplate()
    {
        var template = new DataTemplate(typeof(PaletteCommand));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 9, 12, 9));
        border.SetValue(Border.MarginProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(DockPanel.LastChildFillProperty, true);

        var icon = new FrameworkElementFactory(typeof(TextBlock));
        icon.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.Icon)));
        icon.SetValue(TextBlock.FontSizeProperty, 20d);
        icon.SetValue(TextBlock.WidthProperty, 38d);
        icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        icon.SetValue(DockPanel.DockProperty, Dock.Left);
        panel.AppendChild(icon);

        var content = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.BilingualTitle)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.FontSizeProperty, 14d);
        content.AppendChild(title);
        var category = new FrameworkElementFactory(typeof(TextBlock));
        category.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(PaletteCommand.Category)));
        category.SetValue(TextBlock.OpacityProperty, .62d);
        category.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
        content.AppendChild(category);
        panel.AppendChild(content);

        border.AppendChild(panel);
        template.VisualTree = border;
        return template;
    }

    private void Refresh()
    {
        var query = _search.Text.Trim();
        IEnumerable<PaletteCommand> source = _commands;

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source
                .Select(command => (Command: command, Score: command.Score(query)))
                .Where(result => result.Score > 0)
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Command.Display)
                .Select(result => result.Command);
        }
        else
        {
            source = source
                .OrderBy(command => RecentRank(command.Destination))
                .ThenBy(command => command.Category)
                .ThenBy(command => command.Display);
        }

        _visibleCommands.Clear();
        foreach (var command in source.Take(60))
            _visibleCommands.Add(command);

        _count.Text = $"{_visibleCommands.Count} command(s)";
        if (_results.Items.Count > 0)
            _results.SelectedIndex = 0;
    }

    private int RecentRank(string destination)
    {
        var index = _recentDestinations.IndexOf(destination);
        return index < 0 ? int.MaxValue : index;
    }

    private async void SearchOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            _results.Focus();
            if (_results.SelectedIndex < 0) _results.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            await ExecuteSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async void ResultsOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await ExecuteSelectedAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && _results.SelectedIndex <= 0)
        {
            _search.Focus();
            _search.CaretIndex = _search.Text.Length;
            e.Handled = true;
        }
    }

    private async Task ExecuteSelectedAsync()
    {
        if (_results.SelectedItem is not PaletteCommand command) return;
        Remember(command.Destination);
        Close();
        await _execute(command.Destination);
    }

    private void Remember(string destination)
    {
        _recentDestinations.Remove(destination);
        _recentDestinations.Insert(0, destination);
        if (_recentDestinations.Count > 15)
            _recentDestinations.RemoveRange(15, _recentDestinations.Count - 15);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(_recentDestinations));
        }
        catch
        {
            // Command execution must never fail because recent-history persistence failed.
        }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var recent = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StatePath));
            if (recent is not null)
                _recentDestinations.AddRange(recent.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(15));
        }
        catch
        {
            // Ignore corrupt optional UX state.
        }
    }

    private sealed record PaletteCommand(string Display, string Arabic, string Destination, string Category, string Icon)
    {
        public string BilingualTitle => $"{Display} — {Arabic}";

        public int Score(string query)
        {
            var comparison = StringComparison.CurrentCultureIgnoreCase;
            var score = 0;
            if (Display.Equals(query, comparison) || Arabic.Equals(query, comparison)) score += 100;
            if (Display.StartsWith(query, comparison) || Arabic.StartsWith(query, comparison)) score += 50;
            if (Display.Contains(query, comparison) || Arabic.Contains(query, comparison)) score += 30;
            if (Category.Contains(query, comparison)) score += 15;
            if (Destination.Contains(query, comparison)) score += 10;
            return score;
        }
    }
}
