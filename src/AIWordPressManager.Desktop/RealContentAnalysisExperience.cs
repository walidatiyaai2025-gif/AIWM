using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static partial class RealContentAnalysisExperience
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
        Panel.SetZIndex(panel, 48);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning))
                Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();
        main.Explorer.PropertyChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
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
            Width = 405,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 22, 22),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "RealContentAnalysisPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Real content analysis",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Local rules run against the synchronized WordPress snapshot before AI is used.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        var summary = new TextBlock
        {
            Tag = "RealAnalysisSummary",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        };
        stack.Children.Add(summary);

        var findings = new TextBlock
        {
            Tag = "RealAnalysisFindings",
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        stack.Children.Add(findings);

        var actions = new WrapPanel();
        var sync = Button("Synchronize", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
            await main.Explorer.SynchronizeNowAsync();
        });
        sync.Tag = "RealAnalysisSync";
        actions.Children.Add(sync);

        var analyze = Button("Audit and generate", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("SEO Audit");
            if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);

            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
        });
        analyze.Tag = "RealAnalysisRun";
        actions.Children.Add(analyze);

        var review = Button("Review proposals", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            await main.SuggestedChanges.LoadAsync();
        });
        review.Tag = "RealAnalysisReview";
        actions.Children.Add(review);
        stack.Children.Add(actions);

        return shell;
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        panel.Visibility = main.CurrentPage is "SEO Audit" or "Suggested Changes" or "AI Studio"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var posts = main.Explorer.Posts.ToArray();
        var pages = main.Explorer.Pages.ToArray();
        var content = posts.Concat(pages).ToArray();
        var media = main.Explorer.Media.ToArray();
        var result = Analyze(content, media, main.Explorer.Categories, main.Explorer.Tags);

        SetText(panel, "RealAnalysisSummary",
            content.Length == 0 && media.Length == 0
                ? "No synchronized content is available yet."
                : $"Scanned {content.Length} content items and {media.Length} media items • {result.TotalFindings} rule findings");

        SetText(panel, "RealAnalysisFindings",
            content.Length == 0 && media.Length == 0
                ? "Run synchronization first to build the local SQLite snapshot."
                : string.Join("\n",
                    $"• Missing/weak titles: {result.WeakTitles}",
                    $"• Titles outside 30–65 characters: {result.TitleLengthIssues}",
                    $"• Missing slugs: {result.MissingSlugs}",
                    $"• Thin content under 300 words: {result.ThinContent}",
                    $"• Missing excerpts: {result.MissingExcerpts}",
                    $"• Media metadata issues: {result.MediaMetadataIssues}",
                    $"• Empty categories/tags: {result.EmptyTaxonomies}"));

        var hasSnapshot = content.Length > 0 || media.Length > 0;
        SetEnabled(panel, "RealAnalysisSync", !main.IsOperationRunning);
        SetEnabled(panel, "RealAnalysisRun", hasSnapshot && !main.IsOperationRunning);
        SetEnabled(panel, "RealAnalysisReview", main.SuggestedChanges.Items.Count > 0 && !main.IsOperationRunning);
    }

    private static LocalAnalysisResult Analyze(
        IReadOnlyCollection<WordPressContentItem> content,
        IReadOnlyCollection<WordPressMediaItem> media,
        IEnumerable<WordPressCategoryItem> categories,
        IEnumerable<WordPressTagItem> tags)
    {
        var weakTitles = content.Count(x => string.IsNullOrWhiteSpace(x.Title) || x.Title.Trim().Length < 10);
        var titleLengthIssues = content.Count(x => !string.IsNullOrWhiteSpace(x.Title) && (x.Title.Trim().Length < 30 || x.Title.Trim().Length > 65));
        var missingSlugs = content.Count(x => string.IsNullOrWhiteSpace(x.Slug));
        var thinContent = content.Count(x => WordCount(x.RenderedContent) < 300);
        var missingExcerpts = content.Count(x => string.IsNullOrWhiteSpace(StripHtml(x.RenderedExcerpt)));
        var mediaMetadataIssues = media.Count(x =>
            string.IsNullOrWhiteSpace(x.Title)
            || string.IsNullOrWhiteSpace(x.Slug)
            || x.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase));
        var emptyTaxonomies = categories.Count(x => x.Count == 0) + tags.Count(x => x.Count == 0);
        return new LocalAnalysisResult(weakTitles, titleLengthIssues, missingSlugs, thinContent, missingExcerpts, mediaMetadataIssues, emptyTaxonomies);
    }

    private static int WordCount(string html)
    {
        var text = StripHtml(html);
        return Regex.Matches(text, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant).Count;
    }

    private static string StripHtml(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : System.Net.WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")).Trim();

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(11, 7, 11, 7)
        };
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

    private sealed record LocalAnalysisResult(
        int WeakTitles,
        int TitleLengthIssues,
        int MissingSlugs,
        int ThinContent,
        int MissingExcerpts,
        int MediaMetadataIssues,
        int EmptyTaxonomies)
    {
        public int TotalFindings => WeakTitles + TitleLengthIssues + MissingSlugs + ThinContent + MissingExcerpts + MediaMetadataIssues + EmptyTaxonomies;
    }
}
