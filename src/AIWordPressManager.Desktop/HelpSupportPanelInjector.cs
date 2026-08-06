using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop;

internal static class HelpSupportPanelInjector
{
    private const string HelpHeading = "Help & User Guide";
    private static readonly ConditionalWeakTable<Window, object> InjectedWindows = new();

    public static void EnsureInjected(Window window)
    {
        if (InjectedWindows.TryGetValue(window, out _))
            return;

        _ = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (InjectedWindows.TryGetValue(window, out _))
                    return;

                var rootPanel = FindHelpRootPanel(window);
                if (rootPanel is null)
                    return;

                rootPanel.Children.Insert(Math.Min(2, rootPanel.Children.Count), BuildSupportCard(window));
                InjectedWindows.Add(window, new object());
            }));
    }

    private static Border BuildSupportCard(Window window)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 18),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };

        if (window.TryFindResource("CardStyle") is Style cardStyle)
            card.Style = cardStyle;
        else
        {
            card.Background = window.TryFindResource("SurfaceBrush") as Brush;
            card.BorderBrush = window.TryFindResource("BorderBrush") as Brush;
        }

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Support & Diagnostics",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        };
        layout.Children.Add(heading);

        var description = new TextBlock
        {
            Text = "Create a sanitized support package, verify its SHA-256 integrity and build compatibility, or open the latest package.",
            Margin = new Thickness(0, 8, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = window.TryFindResource("TextSecondaryBrush") as Brush
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        var actions = new WrapPanel();
        actions.Children.Add(CreateButton(window, "Create support bundle", "Help.CreateSupportBundleCommand", primary: true));
        actions.Children.Add(CreateButton(window, "Verify latest bundle", "Help.VerifyLatestSupportBundleCommand"));
        actions.Children.Add(CreateButton(window, "Open latest bundle", "Help.OpenLatestSupportBundleCommand"));
        actions.Children.Add(CreateButton(window, "Open support folder", "Help.OpenSupportFolderCommand"));
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);

        card.Child = layout;
        return card;
    }

    private static Button CreateButton(Window window, string text, string commandPath, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 10, 8),
            Padding = new Thickness(14, 8, 14, 8),
            MinWidth = 155
        };

        var styleName = primary ? "PrimaryButtonStyle" : "SecondaryButtonStyle";
        if (window.TryFindResource(styleName) is Style style)
            button.Style = style;

        button.SetBinding(
            Button.CommandProperty,
            new Binding(commandPath) { Mode = BindingMode.OneWay });
        return button;
    }

    private static StackPanel? FindHelpRootPanel(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock &&
                string.Equals(textBlock.Text, HelpHeading, StringComparison.Ordinal))
            {
                return FindAncestorStackPanel(textBlock);
            }

            var nested = FindHelpRootPanel(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static StackPanel? FindAncestorStackPanel(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is StackPanel stackPanel && stackPanel.Margin.Left >= 30)
                return stackPanel;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
