using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class OperationHistoryPopupExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => InstallHistory(window)));
    }

    private static void InstallHistory(MainWindow window)
    {
        var waitMessage = FindTextBlock(
            window,
            text => text.StartsWith("Please wait. Navigation", StringComparison.OrdinalIgnoreCase));

        if (waitMessage is null)
            return;

        var host = FindAncestor<StackPanel>(waitMessage);
        if (host is null || host.Children.OfType<FrameworkElement>().Any(x => Equals(x.Tag, "OperationHistoryPanel")))
            return;

        var separator = new Border
        {
            Tag = "OperationHistoryPanel",
            Height = 1,
            Margin = new Thickness(0, 14, 0, 10),
            Background = ResolveBrush(window, "BorderBrush", Brushes.LightGray)
        };
        host.Children.Add(separator);

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        headerGrid.Children.Add(new TextBlock
        {
            Text = "Execution steps — latest first",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = ResolveBrush(window, "TextPrimaryBrush", Brushes.Black)
        });

        var hint = new TextBlock
        {
            Text = "Scroll to review earlier steps",
            FontSize = 10,
            Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.DimGray),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(hint, 1);
        headerGrid.Children.Add(hint);
        host.Children.Add(headerGrid);

        var list = new ListBox
        {
            Tag = "OperationHistoryList",
            MaxHeight = 190,
            MinHeight = 86,
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.LightGray),
            Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke),
            Padding = new Thickness(5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsTabStop = false
        };
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Operations.History"));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));
        textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        textFactory.SetValue(TextBlock.FontSizeProperty, 11d);
        textFactory.SetValue(TextBlock.MarginProperty, new Thickness(5, 4, 5, 4));
        textFactory.SetValue(TextBlock.ForegroundProperty, ResolveBrush(window, "TextPrimaryBrush", Brushes.Black));
        list.ItemTemplate = new DataTemplate { VisualTree = textFactory };

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 3)));
        itemStyle.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        list.ItemContainerStyle = itemStyle;

        host.Children.Add(list);
    }

    private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
        => element.TryFindResource(key) is Brush brush ? brush : fallback;

    private static TextBlock? FindTextBlock(DependencyObject parent, Func<string, bool> predicate)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock && predicate(textBlock.Text ?? string.Empty))
                return textBlock;

            var nested = FindTextBlock(child, predicate);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
