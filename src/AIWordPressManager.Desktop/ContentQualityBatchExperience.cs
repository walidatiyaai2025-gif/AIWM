using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static partial class ContentQualityBatchExperience
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
        var panel = BuildPanel(main);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 51);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)) Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();
        main.Explorer.PropertyChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border BuildPanel(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Width = 450,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "ContentQualityBatchPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Content quality batch tools",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Detect duplicate content and prepare reviewable media alt-text suggestions from the synchronized snapshot.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "ContentQualitySummary",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Tag = "ContentQualityDetails",
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        var actions = new WrapPanel();
        var duplicateReport = Button("Export duplicate report", () => ExportDuplicateReportAsync(main));
        duplicateReport.Tag = "DuplicateReportButton";
        actions.Children.Add(duplicateReport);

        var mediaSuggestions = Button("Export alt suggestions", () => ExportMediaSuggestionsAsync(main));
        mediaSuggestions.Tag = "MediaSuggestionButton";
        actions.Children.Add(mediaSuggestions);

        var review = Button("Open review queue", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
        });
        review.Tag = "OpenQualityReviewButton";
        actions.Children.Add(review);
        stack.Children.Add(actions);
        return shell;
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        panel.Visibility = main.CurrentPage is "AI Studio" or "SEO Audit" or "WordPress Explorer" or "Suggested Changes"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var content = main.Explorer.Posts.Concat(main.Explorer.Pages).Take(250).ToArray();
        var media = main.Explorer.Media.ToArray();
        var duplicates = FindDuplicates(content);
        var altSuggestions = BuildAltSuggestions(media);

        SetText(panel, "ContentQualitySummary",
            content.Length == 0 && media.Length == 0
                ? "No synchronized snapshot is available."
                : $"{duplicates.Count} duplicate/near-duplicate pair(s) • {altSuggestions.Count} missing-alt suggestion(s)");
        SetText(panel, "ContentQualityDetails",
            content.Length == 0 && media.Length == 0
                ? "Synchronize the selected website first."
                : string.Join("\n",
                    $"• Exact duplicate pairs: {duplicates.Count(x => x.MatchType == "Exact")}",
                    $"• Near-duplicate pairs (≥82%): {duplicates.Count(x => x.MatchType == "Near")}",
                    $"• Images missing alt text: {altSuggestions.Count}",
                    "• Exports are review files only; WordPress is not modified."));

        SetEnabled(panel, "DuplicateReportButton", content.Length > 1 && !main.IsOperationRunning);
        SetEnabled(panel, "MediaSuggestionButton", altSuggestions.Count > 0 && !main.IsOperationRunning);
        SetEnabled(panel, "OpenQualityReviewButton", !main.IsOperationRunning);
    }

    private static Task ExportDuplicateReportAsync(MainWindowViewModel main)
    {
        var content = main.Explorer.Posts.Concat(main.Explorer.Pages).Take(250).ToArray();
        var rows = FindDuplicates(content);
        var path = CreateReportPath("DuplicateContent", "duplicate-content");
        var csv = new StringBuilder("MatchType,Similarity,FirstId,FirstTitle,FirstUrl,SecondId,SecondTitle,SecondUrl\r\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(',',
                Csv(row.MatchType),
                row.Similarity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                row.First.Id,
                Csv(row.First.Title),
                Csv(row.First.Link),
                row.Second.Id,
                Csv(row.Second.Title),
                Csv(row.Second.Link)));
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        OpenFile(path);
        return Task.CompletedTask;
    }

    private static Task ExportMediaSuggestionsAsync(MainWindowViewModel main)
    {
        var suggestions = BuildAltSuggestions(main.Explorer.Media);
        var path = CreateReportPath("MediaSuggestions", "media-alt-suggestions");
        var csv = new StringBuilder("MediaId,Title,SourceUrl,CurrentAltText,SuggestedAltText,Width,Height,FileSizeBytes\r\n");
        foreach (var item in suggestions)
            csv.AppendLine(string.Join(',',
                item.Media.Id,
                Csv(item.Media.Title),
                Csv(item.Media.SourceUrl),
                Csv(item.Media.AltText),
                Csv(item.Suggestion),
                item.Media.Width?.ToString() ?? string.Empty,
                item.Media.Height?.ToString() ?? string.Empty,
                item.Media.FileSizeBytes?.ToString() ?? string.Empty));
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        OpenFile(path);
        return Task.CompletedTask;
    }

    private static List<DuplicatePair> FindDuplicates(IReadOnlyList<WordPressContentItem> content)
    {
        var normalized = content
            .Select(x => new NormalizedContent(x, NormalizeText(x.RenderedContent), Tokenize(x.RenderedContent)))
            .Where(x => x.Text.Length >= 120 && x.Tokens.Count >= 40)
            .ToArray();
        var rows = new List<DuplicatePair>();
        for (var i = 0; i < normalized.Length; i++)
        {
            for (var j = i + 1; j < normalized.Length; j++)
            {
                var first = normalized[i];
                var second = normalized[j];
                if (first.Text == second.Text)
                {
                    rows.Add(new DuplicatePair("Exact", 1d, first.Item, second.Item));
                    continue;
                }
                var similarity = Jaccard(first.Tokens, second.Tokens);
                if (similarity >= 0.82d)
                    rows.Add(new DuplicatePair("Near", similarity, first.Item, second.Item));
            }
        }
        return rows.OrderByDescending(x => x.Similarity).ToList();
    }

    private static List<MediaAltSuggestion> BuildAltSuggestions(IEnumerable<WordPressMediaItem> media) => media
        .Where(x => x.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(x.AltText))
        .Select(x => new MediaAltSuggestion(x, BuildAltText(x)))
        .Where(x => !string.IsNullOrWhiteSpace(x.Suggestion))
        .ToList();

    private static string BuildAltText(WordPressMediaItem media)
    {
        var source = !string.IsNullOrWhiteSpace(media.Title) && !media.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase)
            ? media.Title
            : !string.IsNullOrWhiteSpace(media.Slug)
                ? media.Slug
                : Path.GetFileNameWithoutExtension(media.OriginalFileName ?? media.SourceUrl);
        if (string.IsNullOrWhiteSpace(source)) return string.Empty;
        var value = Regex.Replace(source, "[-_]+", " ");
        value = Regex.Replace(value, @"\b(?:img|image|photo|dsc|screenshot)[ -]?\d*\b", " ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        if (value.Length > 125) value = value[..125].Trim();
        return value;
    }

    private static string NormalizeText(string html)
    {
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? string.Empty, "<[^>]+>", " "));
        text = Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static HashSet<string> Tokenize(string html) => NormalizeText(html)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length > 2)
        .ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count == 0 || second.Count == 0) return 0d;
        var intersection = first.Count(second.Contains);
        var union = first.Count + second.Count - intersection;
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static string CreateReportPath(string folder, string prefix)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIWordPressManager", "Reports", folder);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    private static void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { Clipboard.SetText(path); }
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(11, 7, 11, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
    }

    private static void SetEnabled(DependencyObject root, string tag, bool value)
    {
        var button = Find<Button>(root, tag);
        if (button is not null) button.IsEnabled = value;
    }

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

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record NormalizedContent(WordPressContentItem Item, string Text, HashSet<string> Tokens);
    private sealed record DuplicatePair(string MatchType, double Similarity, WordPressContentItem First, WordPressContentItem Second);
    private sealed record MediaAltSuggestion(WordPressMediaItem Media, string Suggestion);
}
