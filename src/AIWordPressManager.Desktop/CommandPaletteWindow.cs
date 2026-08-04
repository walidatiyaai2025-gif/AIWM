using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIWordPressManager.Desktop;

public sealed class CommandPaletteWindow : Window
{
    public const string OptimizeMemoryCommand = "__OPTIMIZE_MEMORY__";
    public const string CleanMemoryCommand = "__CLEAN_MEMORY__";
    private readonly Func<string, Task> _execute;
    private readonly TextBox _search = new();
    private readonly ListBox _results = new();
    private readonly List<PaletteCommand> _commands =
    [
        new("Dashboard", "لوحة التحكم", "Dashboard"),
        new("Sites", "المواقع", "Sites"),
        new("WordPress Explorer", "مستكشف ووردبريس", "WordPress Explorer"),
        new("Content Audit", "فحص المحتوى", "Content Audit"),
        new("SEO Audit", "فحص السيو", "SEO Audit"),
        new("Suggested Changes", "التغييرات المقترحة", "Suggested Changes"),
        new("Execution Center", "مركز التنفيذ", "Execution Center"),
        new("Performance & Memory", "الأداء وتنظيف الذاكرة", "Performance"),
        new("Clean memory now", "تنظيف الذاكرة الآن", CleanMemoryCommand),
        new("Database Backup & Restore", "نسخ واستعادة قاعدة البيانات", "Backups"),
        new("Reports", "التقارير", "Reports"),
        new("Logs", "السجلات", "Logs"),
        new("Settings", "الإعدادات", "Settings"),
        new("Help & User Guide", "دليل المستخدم", "Help"),
        new("Release hidden grid caches", "تحرير ذاكرة الجداول المخفية", OptimizeMemoryCommand)
    ];

    public CommandPaletteWindow(Func<string, Task> execute)
    {
        _execute = execute;
        Title = "Command Palette — لوحة الأوامر";
        Width = 650;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _search.Height = 40;
        _search.FontSize = 15;
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.ToolTip = "Type a screen or command in Arabic or English";
        _search.TextChanged += (_, _) => Refresh();
        _search.KeyDown += SearchOnKeyDown;

        _results.Margin = new Thickness(0, 12, 0, 0);
        _results.MouseDoubleClick += async (_, _) => await ExecuteSelectedAsync();
        _results.DisplayMemberPath = nameof(PaletteCommand.Display);
        Grid.SetRow(_results, 1);
        root.Children.Add(_search);
        root.Children.Add(_results);
        Content = root;

        Loaded += (_, _) => { Refresh(); _search.Focus(); };
    }

    private void Refresh()
    {
        var q = _search.Text.Trim();
        _results.ItemsSource = string.IsNullOrWhiteSpace(q)
            ? _commands
            : _commands.Where(c => c.Display.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                   c.Arabic.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                                   c.Destination.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (_results.Items.Count > 0) _results.SelectedIndex = 0;
    }

    private async void SearchOnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { _results.Focus(); if (_results.SelectedIndex < 0) _results.SelectedIndex = 0; e.Handled = true; }
        else if (e.Key == Key.Enter) { await ExecuteSelectedAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }

    private async Task ExecuteSelectedAsync()
    {
        if (_results.SelectedItem is not PaletteCommand command) return;
        Close();
        await _execute(command.Destination);
    }

    private sealed record PaletteCommand(string Display, string Arabic, string Destination)
    {
        public override string ToString() => $"{Display} — {Arabic}";
    }
}
