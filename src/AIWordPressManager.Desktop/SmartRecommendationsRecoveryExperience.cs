using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class SmartRecommendationsRecoveryExperience
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

        var host = FindTopBar(root);
        if (host is null) return;

        var recommendations = HeaderButton("★ Recommendations", "Rank suggestions by impact, confidence, risk and execution readiness");
        recommendations.Click += async (_, _) => await ShowRecommendationsAsync(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), recommendations);

        var recovery = HeaderButton("↶ Recovery", "Review failed and interrupted WordPress transactions");
        recovery.Click += async (_, _) => await ShowRecoveryAsync(window, main);
        host.Children.Insert(Math.Max(0, host.Children.Count - 1), recovery);

        window.PreviewKeyDown += async (_, args) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (args.Key == Key.R)
            {
                args.Handled = true;
                await ShowRecommendationsAsync(window, main);
            }
            else if (args.Key == Key.U)
            {
                args.Handled = true;
                await ShowRecoveryAsync(window, main);
            }
        };
    }

    private static async Task ShowRecommendationsAsync(Window owner, MainWindowViewModel main)
    {
        if (main.Sites.SelectedSite is null)
        {
            MessageBox.Show(owner, "Select a website first.", "Smart recommendations", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (main.SuggestedChanges.Items.Count == 0 && main.SuggestedChanges.RefreshCommand.CanExecute(null))
            await main.SuggestedChanges.RefreshCommand.ExecuteAsync(null);

        var ranked = main.SuggestedChanges.Items
            .Select(item => new RankedRecommendation(item, CalculateImpact(item), BuildImpactReason(item)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Confidence)
            .ThenBy(x => x.Item.RiskLevel, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

        var dialog = CreateDialog(owner, "Smart recommendations", 940, 680);
        var root = new DockPanel { Margin = new Thickness(18) };
        dialog.Content = root;

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var review = ActionButton("Open review queue");
        review.Click += async (_, _) =>
        {
            dialog.Close();
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
            main.SuggestedChanges.SelectedItem = ranked.FirstOrDefault()?.Item;
        };
        footer.Children.Add(review);

        var copy = ActionButton("Copy ranked plan");
        copy.Click += (_, _) => Clipboard.SetText(BuildRecommendationReport(ranked, main.DashboardSelectedSite));
        footer.Children.Add(copy);

        var close = ActionButton("Close");
        close.Click += (_, _) => dialog.Close();
        footer.Children.Add(close);

        var content = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        content.Children.Add(new TextBlock
        {
            Text = "Smart recommendation ranking",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        content.Children.Add(new TextBlock
        {
            Text = ranked.Length == 0
                ? "No proposals are currently available. Run SEO Audit and Generate proposals first."
                : $"Top {ranked.Length} proposal(s), ranked using impact, confidence, risk, staging requirements and direct execution readiness.",
            Margin = new Thickness(0, 4, 0, 14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        foreach (var recommendation in ranked)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("BorderBrush", Brushes.LightGray),
                Background = Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
                CornerRadius = new CornerRadius(7)
            };
            var stack = new StackPanel();
            card.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = $"Impact {recommendation.Score}/100 • {recommendation.Item.ChangeType} • {recommendation.Item.ObjectType} {recommendation.Item.ObjectId}",
                FontWeight = FontWeights.Bold,
                Foreground = Brush("TextPrimaryBrush", Brushes.Black)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"Confidence {recommendation.Item.Confidence:P0} • Risk {recommendation.Item.RiskLevel} • Status {recommendation.Item.ApprovalStatus}",
                Margin = new Thickness(0, 3, 0, 3),
                Foreground = Brush("PrimaryBrush", Brushes.Teal)
            });
            stack.Children.Add(new TextBlock
            {
                Text = recommendation.Reason,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
            });
            content.Children.Add(card);
        }

        dialog.ShowDialog();
    }

    private static async Task ShowRecoveryAsync(Window owner, MainWindowViewModel main)
    {
        var dialog = CreateDialog(owner, "Selective recovery access", 620, 430);
        var root = new StackPanel { Margin = new Thickness(22) };
        dialog.Content = root;

        root.Children.Add(new TextBlock
        {
            Text = "Transaction recovery",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Choose the transaction state to review. This screen never writes to WordPress. Retry or reconciliation remains inside the verified Transaction and Execution Center workflows.",
            Margin = new Thickness(0, 5, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        foreach (var option in new[]
                 {
                     ("Failed transactions", "Failed", "Review terminal failures and their recorded details."),
                     ("Interrupted transactions", "Interrupted", "Review operations that started without a terminal result."),
                     ("In-progress transactions", "Started", "Review recent transactions still considered active."),
                     ("Committed transactions", "Committed", "Review successfully committed WordPress writes."),
                     ("All transactions", "All states", "Open the complete transaction journal.")
                 })
        {
            var button = new Button
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = option.Item1, FontWeight = FontWeights.Bold },
                        new TextBlock { Text = option.Item3, FontSize = 11, TextWrapping = TextWrapping.Wrap }
                    }
                },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };
            button.Click += async (_, _) =>
            {
                dialog.Close();
                main.TransactionCenter.SelectedFilter = option.Item2;
                main.TransactionCenter.SearchText = string.Empty;
                await main.NavigateCommand.ExecuteAsync("Transaction Center");
                await main.TransactionCenter.LoadAsync();
            };
            root.Children.Add(button);
        }

        dialog.ShowDialog();
        await Task.CompletedTask;
    }

    private static int CalculateImpact(SuggestedChangeItem item)
    {
        var score = 20;
        score += (int)Math.Round(Math.Clamp(item.Confidence, 0d, 1d) * 35d);
        score += item.RiskLevel.ToLowerInvariant() switch
        {
            "low" => 20,
            "medium" => 10,
            "high" => 0,
            _ => 5
        };
        if (item.CanApplyDirectly) score += 15;
        if (!item.RequiresStaging) score += 10;

        var type = item.ChangeType.ToLowerInvariant();
        if (type.Contains("title") || type.Contains("description") || type.Contains("alt")) score += 10;
        if (item.ApprovalStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (item.ExecutionStatus.Equals("Executed", StringComparison.OrdinalIgnoreCase)) score -= 35;
        return Math.Clamp(score, 0, 100);
    }

    private static string BuildImpactReason(SuggestedChangeItem item)
    {
        var reasons = new List<string>();
        reasons.Add(item.Confidence >= .9 ? "very high confidence" : item.Confidence >= .75 ? "good confidence" : "requires careful review");
        reasons.Add(item.CanApplyDirectly ? "direct adapter available" : "specialist workflow required");
        reasons.Add(item.RequiresStaging ? "staging required" : "no staging requirement");
        reasons.Add($"{item.RiskLevel.ToLowerInvariant()} risk");
        return string.Join(" • ", reasons);
    }

    private static string BuildRecommendationReport(IEnumerable<RankedRecommendation> recommendations, string site)
    {
        var builder = new StringBuilder()
            .AppendLine("SMART RECOMMENDATION PLAN")
            .AppendLine($"Site: {site}")
            .AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine();

        var index = 1;
        foreach (var item in recommendations)
        {
            builder.AppendLine($"{index++}. Impact {item.Score}/100 | {item.Item.ChangeType} | {item.Item.ObjectType} {item.Item.ObjectId}");
            builder.AppendLine($"   Confidence {item.Item.Confidence:P0} | Risk {item.Item.RiskLevel} | {item.Reason}");
            builder.AppendLine($"   Reason: {item.Item.CleanReason}");
        }
        return builder.ToString();
    }

    private static Window CreateDialog(Window owner, string title, double width, double height) => new()
    {
        Owner = owner,
        Title = title,
        Width = width,
        Height = height,
        MinWidth = 520,
        MinHeight = 360,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.CanResize,
        ShowInTaskbar = false,
        Background = Brush("SurfaceBrush", Brushes.White)
    };

    private static Button HeaderButton(string content, string tooltip) => new()
    {
        Content = content,
        ToolTip = tooltip,
        Margin = new Thickness(5, 0, 0, 0),
        Padding = new Thickness(10, 4, 10, 4),
        MinHeight = 26
    };

    private static Button ActionButton(string content) => new()
    {
        Content = content,
        Margin = new Thickness(7, 0, 0, 0),
        Padding = new Thickness(12, 7, 12, 7),
        MinWidth = 100
    };

    private static StackPanel? FindTopBar(DependencyObject root)
    {
        foreach (var panel in Enumerate<StackPanel>(root))
        {
            if (panel.Orientation != Orientation.Horizontal) continue;
            var texts = panel.Children.OfType<FrameworkElement>()
                .SelectMany(Enumerate<TextBlock>)
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            if (texts.Any(x => x.Contains("Active:", StringComparison.OrdinalIgnoreCase))) return panel;
        }
        return null;
    }

    private static IEnumerable<T> Enumerate<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var nested in Enumerate<T>(child)) yield return nested;
        }
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record RankedRecommendation(SuggestedChangeItem Item, int Score, string Reason);
}
