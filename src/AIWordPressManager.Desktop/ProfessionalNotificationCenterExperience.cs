using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace AIWordPressManager.Desktop;

internal static class ProfessionalNotificationCenterExperience
{
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
        if (sender is not MainWindow window || window.Content is not Grid root)
            return;

        var application = WpfApplication.Current;
        if (application is not null)
        {
            application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
            application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        if (Find<Button>(root, "ProfessionalNotificationButton") is not null)
            return;

        var button = new Button
        {
            Name = "ProfessionalNotificationButton",
            Content = "🔔",
            ToolTip = "Notification Center",
            Width = 42,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 12, 0),
            FontSize = 16,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "BorderStrongBrush");
        button.Click += (_, _) => NotificationHub.Show(window);
        Panel.SetZIndex(button, 9500);
        root.Children.Add(button);

        NotificationHub.Publish("Info", "Workspace ready", "The desktop workspace loaded successfully.", "Application");
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        => NotificationHub.Publish("Error", "Unhandled UI error", e.Exception.ToString(), "WPF Dispatcher");

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = Find<T>(VisualTreeHelper.GetChild(root, i), name);
            if (result is not null) return result;
        }
        return null;
    }
}

internal sealed record NotificationItem(
    Guid Id,
    DateTime CreatedAtUtc,
    string Severity,
    string Title,
    string Message,
    string Source,
    bool IsRead)
{
    public string Summary => $"[{Severity.ToUpperInvariant()}] {Title}";
    public string CreatedDisplay => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}

internal static class NotificationHub
{
    private static readonly ObservableCollection<NotificationItem> Items = [];
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "Notifications", "notifications.json");
    private static bool _loaded;
    private static NotificationCenterWindow? _window;

    internal static void Publish(string severity, string title, string message, string source)
    {
        EnsureLoaded();
        void Add()
        {
            Items.Insert(0, new NotificationItem(Guid.NewGuid(), DateTime.UtcNow, severity, title, message, source, false));
            while (Items.Count > 500) Items.RemoveAt(Items.Count - 1);
            Save();
        }

        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Add();
        else dispatcher.Invoke(Add);
    }

    internal static void Show(Window owner)
    {
        EnsureLoaded();
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        _window = new NotificationCenterWindow(Items) { Owner = owner };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    internal static void MarkAllRead()
    {
        var updated = Items.Select(item => item with { IsRead = true }).ToArray();
        Items.Clear();
        foreach (var item in updated) Items.Add(item);
        Save();
    }

    internal static void Clear()
    {
        Items.Clear();
        Save();
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(StorePath)) return;
            var loaded = JsonSerializer.Deserialize<List<NotificationItem>>(File.ReadAllText(StorePath));
            if (loaded is null) return;
            foreach (var item in loaded.OrderByDescending(x => x.CreatedAtUtc).Take(500)) Items.Add(item);
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Items.ToArray(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

internal sealed class NotificationCenterWindow : Window
{
    private readonly ObservableCollection<NotificationItem> _items;
    private readonly ListBox _list = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _summary = new();

    internal NotificationCenterWindow(ObservableCollection<NotificationItem> items)
    {
        _items = items;
        Title = "Notification Center — مركز الإشعارات";
        Width = 880;
        Height = 620;
        MinWidth = 700;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var markRead = CreateButton("Mark all read");
        markRead.Click += (_, _) => { NotificationHub.MarkAllRead(); RefreshSummary(); _list.Items.Refresh(); };
        var clear = CreateButton("Clear all");
        clear.Margin = new Thickness(8, 0, 0, 0);
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Clear all stored notifications?", "Notification Center", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            NotificationHub.Clear();
            _details.Clear();
            RefreshSummary();
        };
        actions.Children.Add(markRead);
        actions.Children.Add(clear);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Notification Center", FontSize = 22, FontWeight = FontWeights.Bold });
        _summary.Margin = new Thickness(0, 4, 0, 0);
        _summary.Opacity = .68;
        titles.Children.Add(_summary);
        header.Children.Add(titles);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.44, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.56, GridUnitType.Star) });

        _list.ItemsSource = _items;
        _list.DisplayMemberPath = nameof(NotificationItem.Summary);
        _list.SelectionChanged += (_, _) => ShowSelected();
        body.Children.Add(_list);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Padding = new Thickness(14);
        Grid.SetColumn(_details, 2);
        body.Children.Add(_details);

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;

        Loaded += (_, _) =>
        {
            RefreshSummary();
            if (_items.Count > 0) _list.SelectedIndex = 0;
        };
    }

    private static Button CreateButton(string text)
    {
        var button = new Button { Content = text, MinWidth = 110, Height = 38, Padding = new Thickness(14, 8, 14, 8) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        return button;
    }

    private void RefreshSummary()
        => _summary.Text = $"{_items.Count:N0} total • {_items.Count(x => !x.IsRead):N0} unread";

    private void ShowSelected()
    {
        if (_list.SelectedItem is not NotificationItem item)
        {
            _details.Clear();
            return;
        }

        _details.Text = $"{item.Title}\n\nSeverity: {item.Severity}\nSource: {item.Source}\nTime: {item.CreatedDisplay}\n\n{item.Message}";
    }
}
