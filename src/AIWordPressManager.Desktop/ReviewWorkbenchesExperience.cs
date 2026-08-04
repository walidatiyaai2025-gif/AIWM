using System.Diagnostics;
using System.IO;
using System.Net;
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

internal static class ReviewWorkbenchesExperience
{
    private static readonly ConditionalWeakTable<MainWindow, State> Attached = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        var state = new State();
        Attached.Add(window, state);
        var panel = BuildPanel(main, state);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 54);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main, state);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage)
                or nameof(MainWindowViewModel.IsOperationRunning)) Refresh();
        };
        main.Explorer.PropertyChanged += (_, _) => Refresh();
        main.Sites.SelectedSiteChanged += (_, _) =>
        {
            state.Reset();
            Refresh();
        };

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border BuildPanel(MainWindowViewModel main, State state)
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
            Tag = "ReviewWorkbenchesPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock
        {
            Text = "Review workbenches",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Review missing image alt text and duplicate content without changing WordPress.",
            Margin = new Thickness(0, 4, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });

        stack.Children.Add(Heading("Alt Text Workbench"));
        stack.Children.Add(Text("AltSummary", true));
        stack.Children.Add(Text("AltSuggestion", false));
        var altActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) };
        altActions.Children.Add(Button("Next missing alt", () => { state.NextAlt(); RefreshPanel(shell, main, state); }));
        altActions.Children.Add(Button("Copy suggestion", () => Copy(state.CurrentAltSuggestion)));
        altActions.Children.Add(Button("Open image", () => OpenUrl(state.CurrentMedia?.SourceUrl)));
        stack.Children.Add(altActions);

        stack.Children.Add(Heading("Duplicate Content Workbench"));
        stack.Children.Add(Text("DuplicateSummary", true));
        stack.Children.Add(Text("DuplicateDetails", false));
        var duplicateActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        duplicateActions.Children.Add(Button("Next duplicate pair", () => { state.NextDuplicate(); RefreshPanel(shell, main, state); }));
        duplicateActions.Children.Add(Button("Open first", () => OpenUrl(state.CurrentDuplicate?.A.Link)));
        duplicateActions.Children.Add(Button("Open second", () => OpenUrl(state.CurrentDuplicate?.B.Link)));
        duplicateActions.Children.Add(Button("Copy comparison", () => Copy(BuildComparison(state.CurrentDuplicate))));
        stack.Children.Add(duplicateActions);
        return shell;
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main, State state)
    {
        panel.Visibility = main.CurrentPage is "WordPress Explorer" or "SEO Audit" or "AI Studio" or "Suggested Changes"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        state.Update(main.Explorer.Media.ToArray(), main.Explorer.Posts.Concat(main.Explorer.Pages).ToArray());

        SetText(panel, "AltSummary", state.MissingAlt.Count == 0
            ? "No image with missing Alt Text is available in the synchronized snapshot."
            : $"Missing Alt Text: {state.MissingAlt.Count} • Current: {state.AltIndex + 1}/{state.MissingAlt.Count}");
        SetText(panel, "AltSuggestion", state.CurrentMedia is null
            ? "Synchronize the media library first."
            : string.Join("\n",
                $"Media #{state.CurrentMedia.Id}: {state.CurrentMedia.Title}",
                $"Current Alt: {(string.IsNullOrWhiteSpace(state.CurrentMedia.AltText) ? "(empty)" : state.CurrentMedia.AltText)}",
                $"Suggested Alt: {state.CurrentAltSuggestion}",
                $"Dimensions: {state.CurrentMedia.Width?.ToString() ?? "?"} × {state.CurrentMedia.Height?.ToString() ?? "?"}"));

        SetText(panel, "DuplicateSummary", state.Duplicates.Count == 0
            ? "No exact or near-duplicate content pair was found."
            : $"Duplicate pairs: {state.Duplicates.Count} • Current: {state.DuplicateIndex + 1}/{state.Duplicates.Count}");
        SetText(panel, "DuplicateDetails", state.CurrentDuplicate is null
            ? "At least two sufficiently long posts or pages are required."
            : string.Join("\n",
                $"{state.CurrentDuplicate.Kind} • Similarity {state.CurrentDuplicate.Similarity:P0}",
                $"A: #{state.CurrentDuplicate.A.Id} — {state.CurrentDuplicate.A.Title}",
                $"B: #{state.CurrentDuplicate.B.Id} — {state.CurrentDuplicate.B.Title}"));
    }

    private static string BuildAltSuggestion(WordPressMediaItem media)
    {
        var source = !string.IsNullOrWhiteSpace(media.Title) && !media.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase)
            ? media.Title
            : !string.IsNullOrWhiteSpace(media.Slug)
                ? media.Slug
                : Path.GetFileNameWithoutExtension(media.OriginalFileName ?? media.SourceUrl);
        source = Regex.Replace(source ?? string.Empty, @"[-_]+", " ");
        source = Regex.Replace(source, @"\b(image|img|photo|picture|screenshot|download|dsc|scan)\b", " ", RegexOptions.IgnoreCase);
        source = Regex.Replace(source, @"\b\d{4,}\b", " ");
        source = Regex.Replace(source, @"\s+", " ").Trim(' ', '-', '_', '.');
        if (string.IsNullOrWhiteSpace(source)) source = "Website image";
        if (source.Length > 125) source = source[..125].Trim();
        return source;
    }

    private static IReadOnlyList<DuplicatePair> FindDuplicates(IReadOnlyList<WordPressContentItem> content)
    {
        var items = content.Take(250)
            .Select(x => new NormalizedContent(x, Normalize(x.RenderedContent)))
            .Where(x => x.Text.Length >= 250)
            .ToArray();
        var results = new List<DuplicatePair>();
        for (var i = 0; i < items.Length; i++)
        for (var j = i + 1; j < items.Length; j++)
        {
            if (items[i].Text == items[j].Text)
            {
                results.Add(new(items[i].Item, items[j].Item, 1, "Exact duplicate"));
                continue;
            }
            var similarity = Jaccard(items[i].Text, items[j].Text);
            if (similarity >= 0.82) results.Add(new(items[i].Item, items[j].Item, similarity, "Near duplicate"));
        }
        return results.OrderByDescending(x => x.Similarity).Take(100).ToArray();
    }

    private static string Normalize(string html)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html ?? string.Empty, "<[^>]+>", " "));
        text = Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static double Jaccard(string first, string second)
    {
        var a = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var b = second.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static string BuildComparison(DuplicatePair? pair)
    {
        if (pair is null) return "No duplicate pair selected.";
        return string.Join(Environment.NewLine,
            $"Type: {pair.Kind}",
            $"Similarity: {pair.Similarity:P1}",
            string.Empty,
            $"A #{pair.A.Id}: {pair.A.Title}",
            pair.A.Link,
            string.Empty,
            $"B #{pair.B.Id}: {pair.B.Title}",
            pair.B.Link);
    }

    private static TextBlock Heading(string value) => new()
    {
        Text = value,
        Margin = new Thickness(0, 4, 0, 4),
        FontWeight = FontWeights.Bold,
        Foreground = Brush("TextPrimaryBrush", Brushes.Black)
    };

    private static TextBlock Text(string tag, bool bold) => new()
    {
        Tag = tag,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brush(bold ? "TextPrimaryBrush" : "TextSecondaryBrush", bold ? Brushes.Black : Brushes.DimGray)
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(10, 6, 10, 6)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static void Copy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { Clipboard.SetText(value); } catch { }
    }

    private static void SetText(DependencyObject root, string tag, string value)
    {
        var text = Find<TextBlock>(root, tag);
        if (text is not null) text.Text = value;
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

    private sealed class State
    {
        public IReadOnlyList<WordPressMediaItem> MissingAlt { get; private set; } = [];
        public IReadOnlyList<DuplicatePair> Duplicates { get; private set; } = [];
        public int AltIndex { get; private set; }
        public int DuplicateIndex { get; private set; }
        public WordPressMediaItem? CurrentMedia => MissingAlt.Count == 0 ? null : MissingAlt[Math.Clamp(AltIndex, 0, MissingAlt.Count - 1)];
        public string CurrentAltSuggestion => CurrentMedia is null ? string.Empty : BuildAltSuggestion(CurrentMedia);
        public DuplicatePair? CurrentDuplicate => Duplicates.Count == 0 ? null : Duplicates[Math.Clamp(DuplicateIndex, 0, Duplicates.Count - 1)];

        public void Update(IReadOnlyList<WordPressMediaItem> media, IReadOnlyList<WordPressContentItem> content)
        {
            MissingAlt = media.Where(x => x.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(x.AltText)).ToArray();
            Duplicates = FindDuplicates(content);
            if (AltIndex >= MissingAlt.Count) AltIndex = 0;
            if (DuplicateIndex >= Duplicates.Count) DuplicateIndex = 0;
        }

        public void NextAlt() { if (MissingAlt.Count > 0) AltIndex = (AltIndex + 1) % MissingAlt.Count; }
        public void NextDuplicate() { if (Duplicates.Count > 0) DuplicateIndex = (DuplicateIndex + 1) % Duplicates.Count; }
        public void Reset() { AltIndex = 0; DuplicateIndex = 0; MissingAlt = []; Duplicates = []; }
    }

    private sealed record NormalizedContent(WordPressContentItem Item, string Text);
    private sealed record DuplicatePair(WordPressContentItem A, WordPressContentItem B, double Similarity, string Kind);
}
