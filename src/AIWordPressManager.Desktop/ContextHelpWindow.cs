using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

public sealed class ContextHelpWindow : Window
{
    public ContextHelpWindow(string title, string instruction, string screenName)
    {
        Title = $"Help — {title}";
        Width = 610;
        MinHeight = 320;
        MaxHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = (Brush)System.Windows.Application.Current.FindResource("AppBackgroundBrush");
        Foreground = (Brush)System.Windows.Application.Current.FindResource("TextPrimaryBrush");

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(12),
            Background = (Brush)System.Windows.Application.Current.FindResource("PrimaryBrush"),
            Child = new TextBlock
            {
                Text = "?",
                Foreground = (Brush)System.Windows.Application.Current.FindResource("OnAccentBrush"),
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        header.Children.Add(icon);

        var heading = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(screenName) ? "Contextual instruction" : $"Screen: {screenName}",
            Foreground = (Brush)System.Windows.Application.Current.FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        root.Children.Add(header);

        var card = new Border
        {
            Margin = new Thickness(0, 20, 0, 20),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            Background = (Brush)System.Windows.Application.Current.FindResource("SurfaceBrush"),
            BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        var text = new TextBlock
        {
            Text = instruction,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 23
        };
        card.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = text
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock
        {
            Text = "Tip: press F1 or click the ? icon to leave Help Mode.",
            Foreground = (Brush)System.Windows.Application.Current.FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Content = "Close",
            MinWidth = 100,
            Style = (Style)System.Windows.Application.Current.FindResource("PrimaryButtonStyle")
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }
}
