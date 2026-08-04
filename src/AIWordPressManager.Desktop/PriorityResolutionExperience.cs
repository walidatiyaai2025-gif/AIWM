using System.Diagnostics;
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

internal static class PriorityResolutionExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

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

        var state = new State(main);
        Attached.Add(window, state);
        var panel = BuildPanel(state);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 52);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, state);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)) Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => { state.Reset(); Refresh(); };
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

    private static Border BuildPanel(State state)
    {
        var shell = new Border
        {
            Width = 470,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "PriorityResolutionPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Priority resolution workspace",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Prioritizes media fixes and prepares a safe canonical/merge plan for duplicate content.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        stack.Children.Add(SectionTitle("Media priority queue"));
        stack.Children.Add(Text("MediaPrioritySummary"));
        stack.Children.Add(Text("MediaPriorityCurrent", new Thickness(0, 5, 0, 8)));
        var mediaActions = new WrapPanel();
        mediaActions.Children.Add(Button("Next media", "MediaPriorityNext", () => { state.NextMedia(); RefreshPanel(shell, state); }));
        mediaActions.Children.Add(Button("Open media", "MediaPriorityOpen", state.OpenCurrentMedia));
        mediaActions.Children.Add(Button("Copy batch plan", "MediaPriorityCopy", state.CopyMediaPlan));
        stack.Children.Add(mediaActions);

        stack.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
        stack.Children.Add(SectionTitle("Duplicate resolution planner"));
        stack.Children.Add(Text("DuplicatePlanSummary"));
        stack.Children.Add(Text("DuplicatePlanCurrent", new Thickness(0, 5, 0, 8)));
        var duplicateActions = new WrapPanel();
        duplicateActions.Children.Add(Button("Next pair", "DuplicatePlanNext", () => { state.NextDuplicate(); RefreshPanel(shell, state); }));
        duplicateActions.Children.Add(Button("Open canonical", "DuplicatePlanCanonical", state.OpenCanonical));
        duplicateActions.Children.Add(Button("Open duplicate", "DuplicatePlanDuplicate", state.OpenDuplicate));
        duplicateActions.Children.Add(Button("Copy resolution plan", "DuplicatePlanCopy", state.CopyDuplicatePlan));
        stack.Children.Add(duplicateActions);
        return shell;
    }

    private static void RefreshPanel(Border panel, State state)
    {
        var main = state.Main;
        panel.Visibility = main.CurrentPage is "WordPress Explorer" or "SEO Audit" or "AI Studio" or "Suggested Changes"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        state.RefreshQueues();
        var media = state.CurrentMedia;
        SetText(panel, "MediaPrioritySummary",
            state.MediaQueue.Count == 0
                ? "No media priority items are currently detected."
                : $"{state.MediaQueue.Count} media items need attention • sorted by missing Alt Text, file size and dimensions.");
        SetText(panel, "MediaPriorityCurrent", media is null
            ? "Synchronize the site to build the media queue."
            : $"Priority {state.MediaIndex + 1}/{state.MediaQueue.Count} • score {media.Score}\n#{media.Item.Id} {media.Item.Title}\n{media.Reason}");

        var pair = state.CurrentDuplicate;
        SetText(panel, "DuplicatePlanSummary",
            state.DuplicateQueue.Count == 0
                ? "No exact or near-duplicate content pairs are currently detected."
                : $"{state.DuplicateQueue.Count} duplicate pairs detected • canonical selection is based on status, recency and content depth.");
        SetText(panel, "DuplicatePlanCurrent", pair is null
            ? "Synchronize posts and pages to build the duplicate queue."
            : $"Pair {state.DuplicateIndex + 1}/{state.DuplicateQueue.Count} • {pair.Kind} {pair.Similarity:P0}\nCanonical: #{pair.Canonical.Id} {pair.Canonical.Title}\nDuplicate: #{pair.Duplicate.Id} {pair.Duplicate.Title}");

        var idle = !main.IsOperationRunning;
        SetEnabled(panel, "MediaPriorityNext", state.MediaQueue.Count > 0 && idle);
        SetEnabled(panel, "MediaPriorityOpen", media is not null && idle);
        SetEnabled(panel, "MediaPriorityCopy", state.MediaQueue.Count > 0 && idle);
        SetEnabled(panel, "DuplicatePlanNext", state.DuplicateQueue.Count > 0 && idle);
        SetEnabled(panel, "DuplicatePlanCanonical", pair is not null && idle);
        SetEnabled(panel, "DuplicatePlanDuplicate", pair is not null && idle);
        SetEnabled(panel, "DuplicatePlanCopy", pair is not null && idle);
    }

    private sealed class State(MainWindowViewModel main)
    {
        public MainWindowViewModel Main { get; } = main;
        public List<MediaPriorityItem> MediaQueue { get; private set; } = [];
        public List<DuplicatePair> DuplicateQueue { get; private set; } = [];
        public int MediaIndex { get; private set; }
        public int DuplicateIndex { get; private set; }
        public MediaPriorityItem? CurrentMedia => MediaQueue.Count == 0 ? null : MediaQueue[Math.Clamp(MediaIndex, 0, MediaQueue.Count - 1)];
        public DuplicatePair? CurrentDuplicate => DuplicateQueue.Count == 0 ? null : DuplicateQueue[Math.Clamp(DuplicateIndex, 0, DuplicateQueue.Count - 1)];

        public void Reset() { MediaIndex = 0; DuplicateIndex = 0; MediaQueue = []; DuplicateQueue = []; }

        public void RefreshQueues()
        {
            var mediaId = CurrentMedia?.Item.Id;
            MediaQueue = Main.Explorer.Media
                .Select(BuildMediaPriority)
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            MediaIndex = mediaId is null ? Math.Min(MediaIndex, Math.Max(0, MediaQueue.Count - 1)) : Math.Max(0, MediaQueue.FindIndex(x => x.Item.Id == mediaId));

            var pairKey = CurrentDuplicate is null ? null : $"{CurrentDuplicate.Canonical.Id}:{CurrentDuplicate.Duplicate.Id}";
            DuplicateQueue = BuildDuplicateQueue(Main.Explorer.Posts.Concat(Main.Explorer.Pages).Take(250).ToArray());
            DuplicateIndex = pairKey is null
                ? Math.Min(DuplicateIndex, Math.Max(0, DuplicateQueue.Count - 1))
                : Math.Max(0, DuplicateQueue.FindIndex(x => $"{x.Canonical.Id}:{x.Duplicate.Id}" == pairKey));
        }

        public void NextMedia() { if (MediaQueue.Count > 0) MediaIndex = (MediaIndex + 1) % MediaQueue.Count; }
        public void NextDuplicate() { if (DuplicateQueue.Count > 0) DuplicateIndex = (DuplicateIndex + 1) % DuplicateQueue.Count; }
        public void OpenCurrentMedia() => OpenUrl(CurrentMedia?.Item.SourceUrl);
        public void OpenCanonical() => OpenUrl(CurrentDuplicate?.Canonical.Link);
        public void OpenDuplicate() => OpenUrl(CurrentDuplicate?.Duplicate.Link);

        public void CopyMediaPlan()
        {
            if (MediaQueue.Count == 0) return;
            var text = new StringBuilder()
                .AppendLine("MEDIA PRIORITY PLAN")
                .AppendLine($"Generated: {DateTime.Now:g}")
                .AppendLine($"Items: {MediaQueue.Count}")
                .AppendLine();
            foreach (var item in MediaQueue.Take(100))
                text.AppendLine($"[{item.Score}] Media #{item.Item.Id} | {item.Item.Title} | {item.Reason} | {item.Item.SourceUrl}");
            Clipboard.SetText(text.ToString());
        }

        public void CopyDuplicatePlan()
        {
            var pair = CurrentDuplicate;
            if (pair is null) return;
            var plan = string.Join(Environment.NewLine,
                "DUPLICATE CONTENT RESOLUTION PLAN",
                $"Type: {pair.Kind}",
                $"Similarity: {pair.Similarity:P0}",
                string.Empty,
                $"Canonical: #{pair.Canonical.Id} {pair.Canonical.Title}",
                pair.Canonical.Link,
                string.Empty,
                $"Duplicate: #{pair.Duplicate.Id} {pair.Duplicate.Title}",
                pair.Duplicate.Link,
                string.Empty,
                "Recommended actions:",
                "1. Review both pages and preserve unique verified information.",
                "2. Merge useful unique sections into the canonical page.",
                "3. Update internal links to point to the canonical URL.",
                "4. Create a 301 redirect from the duplicate URL only after approval.",
                "5. Verify the canonical, redirect and sitemap after publishing.");
            Clipboard.SetText(plan);
        }
    }

    private static MediaPriorityItem BuildMediaPriority(WordPressMediaItem item)
    {
        var score = 0;
        var reasons = new List<string>();
        if (item.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(item.AltText))
        {
            score += 50;
            reasons.Add("missing Alt Text");
        }
        if (item.FileSizeBytes is > 1_000_000)
        {
            score += 35;
            reasons.Add($"large file {item.FileSizeBytes.Value / 1024d / 1024d:F1} MB");
        }
        else if (item.FileSizeBytes is > 500_000)
        {
            score += 20;
            reasons.Add($"file {item.FileSizeBytes.Value / 1024d:F0} KB");
        }
        if (item.Width is > 0 and < 300 || item.Height is > 0 and < 300)
        {
            score += 20;
            reasons.Add($"small dimensions {item.Width}×{item.Height}");
        }
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
            reasons.Add("missing descriptive title");
        }
        return new MediaPriorityItem(item, score, reasons.Count == 0 ? "No priority issue" : string.Join(", ", reasons));
    }

    private static List<DuplicatePair> BuildDuplicateQueue(IReadOnlyList<WordPressContentItem> content)
    {
        var prepared = content
            .Select(x => new PreparedContent(x, Normalize(x.RenderedContent), Tokens(x.RenderedContent)))
            .Where(x => x.Normalized.Length >= 180)
            .ToArray();
        var result = new List<DuplicatePair>();
        for (var i = 0; i < prepared.Length; i++)
        for (var j = i + 1; j < prepared.Length; j++)
        {
            var left = prepared[i];
            var right = prepared[j];
            var exact = left.Normalized.Equals(right.Normalized, StringComparison.Ordinal);
            var similarity = exact ? 1d : Jaccard(left.Tokens, right.Tokens);
            if (!exact && similarity < 0.82d) continue;
            var canonical = SelectCanonical(left.Item, right.Item);
            var duplicate = canonical.Id == left.Item.Id ? right.Item : left.Item;
            result.Add(new DuplicatePair(exact ? "Exact" : "Near duplicate", similarity, canonical, duplicate));
        }
        return result.OrderByDescending(x => x.Similarity).ThenBy(x => x.Canonical.Title).Take(250).ToList();
    }

    private static WordPressContentItem SelectCanonical(WordPressContentItem left, WordPressContentItem right)
    {
        static int Score(WordPressContentItem item)
        {
            var score = item.Status.Equals("publish", StringComparison.OrdinalIgnoreCase) ? 100 : 0;
            score += Math.Min(80, PlainText(item.RenderedContent).Length / 100);
            if (item.ModifiedAt is not null) score += Math.Max(0, 30 - (int)(DateTimeOffset.Now - item.ModifiedAt.Value).TotalDays / 30);
            if (!string.IsNullOrWhiteSpace(item.Link)) score += 10;
            return score;
        }
        return Score(left) >= Score(right) ? left : right;
    }

    private static string Normalize(string html) => Regex.Replace(PlainText(html).ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static HashSet<string> Tokens(string html) => Regex.Matches(Normalize(html), @"[\p{L}\p{N}]{3,}").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
    private static string PlainText(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html ?? string.Empty, "<[^>]+>", " ")).Replace("\r", " ").Replace("\n", " ").Trim();

    private static void OpenUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static TextBlock SectionTitle(string value) => new()
    {
        Text = value,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 2, 0, 4),
        Foreground = Brush("TextPrimaryBrush", Brushes.Black)
    };

    private static TextBlock Text(string tag, Thickness? margin = null) => new()
    {
        Tag = tag,
        Margin = margin ?? new Thickness(0),
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
    };

    private static Button Button(string text, string tag, Action action)
    {
        var button = new Button { Content = text, Tag = tag, Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(11, 7, 11, 7) };
        button.Click += (_, _) => action();
        return button;
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var item = Find<TextBlock>(root, tag);
        if (item is not null) item.Text = value;
    }

    private static void SetEnabled(DependencyObject root, string tag, bool value)
    {
        var item = Find<Button>(root, tag);
        if (item is not null) item.IsEnabled = value;
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

    private static Brush Brush(string key, Brush fallback) => global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record MediaPriorityItem(WordPressMediaItem Item, int Score, string Reason);
    private sealed record PreparedContent(WordPressContentItem Item, string Normalized, HashSet<string> Tokens);
    private sealed record DuplicatePair(string Kind, double Similarity, WordPressContentItem Canonical, WordPressContentItem Duplicate);
}
