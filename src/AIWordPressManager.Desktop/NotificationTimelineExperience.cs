using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class NotificationTimelineExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded),
            true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        Attached.Add(window, new object());

        var panel = CreatePanel(main);
        Grid.SetRowSpan(panel, Math.Max(1, root.RowDefinitions.Count));
        Panel.SetZIndex(panel, 200);
        root.Children.Add(panel);

        var toggle = CreateToggleButton(panel, main);
        Grid.SetRow(toggle, 0);
        Panel.SetZIndex(toggle, 210);
        root.Children.Add(toggle);

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                Toggle(panel);
                args.Handled = true;
            }
        };

        void Refresh() => RefreshPanel(panel, toggle, main);
        main.Operations.History.CollectionChanged += (_, _) => Refresh();
        main.Operations.Operations.CollectionChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Button CreateToggleButton(Border panel, MainWindowViewModel main)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 14, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Content = "🔔  0",
            Tag = "NotificationToggle",
            ToolTip = "Notification Center and Activity Timeline (Ctrl+Shift+N)",
            FontWeight = FontWeights.SemiBold
        };
        button.Click += (_, _) => Toggle(panel);
        return button;
    }

    private static Border CreatePanel(MainWindowViewModel main)
    {
        var panel = new Border
        {
            Width = 430,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 48, 10, 10),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.Gray),
            Background = ResourceBrush("SurfaceBrush", Brushes.White),
            Visibility = Visibility.Collapsed,
            Tag = "NotificationPanel"
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Child = layout;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Notification Center",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
                },
                new TextBlock
                {
                    Text = "Latest workflow events appear first.",
                    Margin = new Thickness(0, 3, 0, 0),
                    FontSize = 11,
                    Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
                }
            }
        });

        var close = new Button { Content = "✕", Padding = new Thickness(8, 4, 8, 4) };
        close.Click += (_, _) => panel.Visibility = Visibility.Collapsed;
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        layout.Children.Add(header);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var copy = new Button { Content = "Copy details", Padding = new Thickness(10, 5, 10, 5) };
        copy.Click += (_, _) => CopyDetails(main.Operations.History);
        actions.Children.Add(copy);

        var clear = new Button
        {
            Content = "Clear completed",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5)
        };
        clear.Click += (_, _) =>
        {
            main.Operations.ClearHistory();
            main.Operations.ClearCompletedOperations();
        };
        actions.Children.Add(clear);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var list = new StackPanel { Tag = "NotificationItems" };
        scroll.Content = list;
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);

        return panel;
    }

    private static void RefreshPanel(Border panel, Button toggle, MainWindowViewModel main)
    {
        var history = main.Operations.History.Take(30).ToArray();
        var unread = history.Count(x => x.State is "Failed" or "Completed" or "Cancelled");
        toggle.Content = unread == 0 ? "🔔  0" : $"🔔  {unread}";

        var list = Find<StackPanel>(panel, "NotificationItems");
        if (list is null) return;

        list.Children.Clear();
        if (history.Length == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No workflow events yet.",
                Margin = new Thickness(4, 18, 4, 4),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
            });
            return;
        }

        foreach (var item in history)
            list.Children.Add(CreateEventCard(item));
    }

    private static Border CreateEventCard(UiOperationHistoryItem item)
    {
        var icon = item.State switch
        {
            "Completed" => "✓",
            "Failed" => "✕",
            "Cancelled" => "■",
            "Started" => "▶",
            _ => "•"
        };

        var accent = item.State switch
        {
            "Completed" => Brushes.SeaGreen,
            "Failed" => Brushes.IndianRed,
            "Cancelled" => Brushes.DarkOrange,
            "Started" => Brushes.DodgerBlue,
            _ => ResourceBrush("PrimaryBrush", Brushes.Teal)
        };

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1, 1, 1, 1),
            BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray),
            Background = ResourceBrush("SurfaceAltBrush", Brushes.WhiteSmoke)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        card.Child = grid;

        grid.Children.Add(new TextBlock
        {
            Text = icon,
            Foreground = accent,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Top
        });

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = $"{item.Step}  •  {item.Progress}%",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextPrimaryBrush", Brushes.Black)
        });
        body.Children.Add(new TextBlock
        {
            Text = item.Detail,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextSecondaryBrush", Brushes.DimGray)
        });
        body.Children.Add(new TextBlock
        {
            Text = $"{item.TimeText}  •  {item.State}",
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 10,
            Foreground = accent
        });
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);
        return card;
    }

    private static void CopyDetails(IEnumerable<UiOperationHistoryItem> history)
    {
        var rows = history.Take(100).ToArray();
        if (rows.Length == 0) return;

        var text = new StringBuilder();
        foreach (var item in rows)
        {
            text.Append(item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            text.Append(" | ").Append(item.State);
            text.Append(" | ").Append(item.Progress).Append('%');
            text.Append(" | ").Append(item.Step);
            text.AppendLine();
            text.AppendLine(item.Detail);
            text.AppendLine();
        }

        Clipboard.SetText(text.ToString());
    }

    private static void Toggle(Border panel) =>
        panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private static T? Find<T>(DependencyObject root, string tag) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && Equals(element.Tag, tag)) return element;
            var nested = Find<T>(child, tag);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static Brush ResourceBrush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
