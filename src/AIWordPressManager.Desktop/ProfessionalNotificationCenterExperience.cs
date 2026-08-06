using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

        if (Application.Current is not null)
            AttachApplicationHandlers(Application.Current);
        else
            EventManager.RegisterClassHandler(
                typeof(Application),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((_, _) => AttachApplicationHandlers(Application.Current)),
                true);
    }

    private static void AttachApplicationHandlers(Application? application)
    {
        if (application is null) return;
        application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        ProfessionalNotificationCenter.Publish(
            NotificationSeverity.Error,
            "Unhandled UI error",
            e.Exception.Message,
            e.Exception.ToString(),
            "WPF Dispatcher");
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window.Content is not Grid root)
            return;

        if (FindNamedElement<Button>(root, "ProfessionalNotificationButton") is not null)
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
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
        button.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        button.SetResourceReference(Control.BorderBrushProperty, "BorderStrongBrush");
        button.Click += (_, _) => ProfessionalNotificationCenter.Show(window);
        Panel.SetZIndex(button, 9500);
        root.Children.Add(button);

        ProfessionalNotificationCenter.Publish(
            NotificationSeverity.Info,
            "Workspace ready",
            "The desktop workspace loaded successfully.",
            null,
            "Application");
    }

    private static T? FindNamedElement<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T element && element.Name == name) return element;
        if (root is not Visual && root is not System.Windows.Media.Media3D.Visual3D) return null;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var result = FindNamedElement<T>(VisualTreeHelper.GetChild(root, index), name);
            if (result is not null) return result;
        }
        return null;
    }
}

internal enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error
}

internal sealed record ProfessionalNotification(
    Guid Id,
    DateTime CreatedAtUtc,
    NotificationSeverity Severity,
    string Title,
    string Message,
    string? Details,
    string Source,
    bool IsRead)
{
    public string CreatedDisplay => CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SeverityDisplay => Severity.ToString().ToUpperInvariant();
    public string Summary => $"[{SeverityDisplay}] {Title}";
}

internal static class ProfessionalNotificationCenter
{
    private static readonly object Sync = new();
    private static readonly ObservableCollection<ProfessionalNotification> Items = [];
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager", "Notifications", "notifications.json");
    private static bool _loaded;
    private static NotificationCenterWindow? _window;

    internal static void Publish(
        NotificationSeverity severity,
        string title,
        string message,
        string? details = null,
        string source = "Application")
    {
        EnsureLoaded();
        Application.Current?.Dispatcher.Invoke(() =>
        {
            lock (Sync)
            {
                Items.Insert(0, new ProfessionalNotification(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    severity,
                    title,
                    message,
                    details,
                    source,
                    false));

                while (Items.Count > 500)
                    Items.RemoveAt(Items.Count - 1);

                Save();
            }
        });
    }

    internal static void Show(Window owner)
    {
        EnsureLoaded();
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        _window = new NotificationCenterWindow(Items)
        {
            Owner = owner
        };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    internal static void MarkAllRead()
    {
        EnsureLoaded();
        lock (Sync)
        {
            var updated = Items.Select(item => item with { IsRead = true }).ToArray();
            Items.Clear();
            foreach (var item in updated) Items.Add(item);
            Save();
        }
    }

    internal static void ClearAll()
    {
        EnsureLoaded();
        lock (Sync)
        {
            Items.Clear();
            Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Sync)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(StorePath)) return;
                var loaded = JsonSerializer.Deserialize<List<ProfessionalNotification>>(File.ReadAllText(StorePath));
                if (loaded is null) return;
                foreach (var item in loaded.OrderByDescending(item => item.CreatedAtUtc).Take(500))
                    Items.Add(item);
            }
            catch
            {
                // A corrupt optional notification history must not prevent application startup.
            }
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Items.ToArray(), new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // Notification persistence is best effort.
        }
    }
}

internal sealed class NotificationCenterWindow : Window
{
    private readonly ObservableCollection<ProfessionalNotification> _items;
    private readonly ListBox _list = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _summary = new();

    internal NotificationCenterWindow(ObservableCollection<ProfessionalNotification> items)
    {
        _items = items;
        Title = "Notification Center — مركز الإشعارات";
        Width = 900;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var markRead = CreateButton("Mark all read");
        markRead.Click += (_, _) =>
        {
            ProfessionalNotificationCenter.MarkAllRead();
            RefreshSummary();
            _list.Items.Refresh();
        };
        var clear = CreateButton("Clear all");
        clear.Margin = new Thickness(8, 0, 0, 0);
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Clear all stored notifications?", "Notification Center",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            ProfessionalNotificationCenter.ClearAll();
            _details.Clear();
            RefreshSummary();
        };
        actions.Children.Add(markRead);
        actions.Children.Add(clear);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = "Notification Center",
            FontSize = 22,
            FontWeight = FontWeights.Bold
        });
        _summary.Margin = new Thickness(0, 4, 0, 0);
        _summary.Opacity = .68;
        titles.Children.Add(_summary);
        header.Children.Add(titles);
        root.Children.Add(header);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.44, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.56, GridUnitType.Star) });

        _list.ItemsSource = _items;
        _list.DisplayMemberPath = nameof(ProfessionalNotification.Summary);
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

        var footer = new TextBlock
        {
            Text = "Notifications are stored locally and limited to the latest 500 entries.",
            Opacity = .6,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Loaded += (_, _) =>
        {
            RefreshSummary();
            if (_items.Count > 0) _list.SelectedIndex = 0;
        };
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Height = 38,
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        return button;
    }

    private void RefreshSummary()
    {
        var unread = _items.Count(item => !item.IsRead);
        _summary.Text = $"{_items.Count:N0} total • {unread:N0} unread";
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not ProfessionalNotification selected)
        {
            _details.Clear();
            return;
        }

        _details.Text = $"{selected.Title}\n\n" +
                        $"Severity: {selected.SeverityDisplay}\n" +
                        $"Source: {selected.Source}\n" +
                        $"Time: {selected.CreatedDisplay}\n\n" +
                        $"{selected.Message}\n\n" +
                        (string.IsNullOrWhiteSpace(selected.Details) ? string.Empty : selected.Details);
    }
}
