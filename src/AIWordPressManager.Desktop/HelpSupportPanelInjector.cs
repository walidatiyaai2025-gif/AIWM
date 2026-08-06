using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class HelpSupportPanelInjector
{
    private const string HelpHeading = "Help & User Guide";
    private const int MaximumInjectionAttempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly ConditionalWeakTable<Window, object> InjectedWindows = new();
    private static readonly ConditionalWeakTable<Window, object> PendingWindows = new();

    public static void EnsureInjected(Window window)
    {
        if (InjectedWindows.TryGetValue(window, out _) || PendingWindows.TryGetValue(window, out _))
            return;

        PendingWindows.Add(window, new object());
        _ = TryInjectAsync(window);
    }

    private static async Task TryInjectAsync(Window window)
    {
        try
        {
            for (var attempt = 1; attempt <= MaximumInjectionAttempts; attempt++)
            {
                if (!window.IsLoaded)
                    await WaitForDispatcherAsync(window.Dispatcher);

                var injected = await window.Dispatcher.InvokeAsync(
                    () => TryInject(window),
                    DispatcherPriority.Loaded);

                if (injected)
                    return;

                await Task.Delay(RetryDelay);
            }
        }
        finally
        {
            PendingWindows.Remove(window);
        }
    }

    private static bool TryInject(Window window)
    {
        if (InjectedWindows.TryGetValue(window, out _))
            return true;

        var rootPanel = FindHelpRootPanel(window);
        if (rootPanel is null)
            return false;

        rootPanel.Children.Insert(Math.Min(2, rootPanel.Children.Count), BuildSupportCard(window));
        InjectedWindows.Add(window, new object());
        return true;
    }

    private static Task WaitForDispatcherAsync(Dispatcher dispatcher)
    {
        return dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded).Task;
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
        for (var index = 0; index < 5; index++)
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
            Text = "Create a sanitized support package, verify its SHA-256 integrity and build compatibility, or copy a complete support summary.",
            Margin = new Thickness(0, 8, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = window.TryFindResource("TextSecondaryBrush") as Brush
        };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        var status = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        status.SetBinding(
            TextBlock.TextProperty,
            new Binding("Help.SupportBundleVerificationStatus") { Mode = BindingMode.OneWay });
        Grid.SetRow(status, 2);
        layout.Children.Add(status);

        var latestPath = new TextBlock
        {
            Foreground = window.TryFindResource("TextSecondaryBrush") as Brush,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 14)
        };
        latestPath.SetBinding(
            TextBlock.TextProperty,
            new Binding("Help.LatestSupportBundlePath") { Mode = BindingMode.OneWay });
        latestPath.SetBinding(
            FrameworkElement.ToolTipProperty,
            new Binding("Help.LatestSupportBundlePath") { Mode = BindingMode.OneWay });
        Grid.SetRow(latestPath, 3);
        layout.Children.Add(latestPath);

        var actions = new WrapPanel();
        actions.Children.Add(CreateButton(window, "Create support bundle", "Help.CreateSupportBundleCommand", primary: true));
        actions.Children.Add(CreateButton(window, "Verify latest bundle", "Help.VerifyLatestSupportBundleCommand"));
        actions.Children.Add(CreateButton(window, "Copy support summary", "Help.CopySupportSummaryCommand"));
        actions.Children.Add(CreateButton(window, "Open latest bundle", "Help.OpenLatestSupportBundleCommand"));
        actions.Children.Add(CreateButton(window, "Open support folder", "Help.OpenSupportFolderCommand"));
        Grid.SetRow(actions, 4);
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

    private static StackPanel? FindHelpRootPanel(Window window)
    {
        if (window.DataContext is MainWindowViewModel viewModel)
        {
            var commandPanel = FindPanelContainingCommand(window, viewModel.Help.OpenGuideCommand);
            if (commandPanel is not null)
                return commandPanel;
        }

        return FindPanelContainingHeading(window);
    }

    private static StackPanel? FindPanelContainingCommand(DependencyObject parent, ICommand expectedCommand)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && ReferenceEquals(button.Command, expectedCommand))
            {
                var panel = FindAncestorStackPanel(button);
                if (panel is not null)
                    return panel;
            }

            var nested = FindPanelContainingCommand(child, expectedCommand);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static StackPanel? FindPanelContainingHeading(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock &&
                string.Equals(textBlock.Text, HelpHeading, StringComparison.Ordinal))
            {
                var panel = FindAncestorStackPanel(textBlock);
                if (panel is not null)
                    return panel;
            }

            var nested = FindPanelContainingHeading(child);
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
