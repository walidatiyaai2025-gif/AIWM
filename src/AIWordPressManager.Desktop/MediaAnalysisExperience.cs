using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class MediaAnalysisExperience
{
    private static readonly ConditionalWeakTable<MainWindow, object> Attached = new();
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "image", "img", "photo", "picture", "screenshot", "untitled", "download", "dsc", "scan"
    };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
        if (Attached.TryGetValue(window, out _)) return;
        if (window.DataContext is not MainWindowViewModel main || window.Content is not Grid root) return;

        Attached.Add(window, new object());
        var panel = BuildPanel(main);
        Grid.SetRow(panel, 3);
        Panel.SetZIndex(panel, 49);
        root.Children.Add(panel);

        void Refresh() => RefreshPanel(panel, main);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.CurrentPage) or nameof(MainWindowViewModel.IsOperationRunning)) Refresh();
        };
        main.Sites.SelectedSiteChanged += (_, _) => Refresh();
        main.Explorer.PropertyChanged += (_, _) => Refresh();

        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Refresh();
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
        Refresh();
    }

    private static Border BuildPanel(MainWindowViewModel main)
    {
        var shell = new Border
        {
            Width = 425,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(22, 0, 0, 22),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush", Brushes.White),
            BorderBrush = Brush("BorderBrush", Brushes.LightGray),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Tag = "MediaAnalysisPanel"
        };

        var stack = new StackPanel();
        shell.Child = stack;
        stack.Children.Add(new TextBlock { Text = "Media quality analysis", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brush("TextPrimaryBrush", Brushes.Black) });
        stack.Children.Add(new TextBlock
        {
            Text = "Analyzes synchronized WordPress media metadata, alt text, dimensions and file size without changing the website.",
            Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        stack.Children.Add(new TextBlock { Tag = "MediaAnalysisSummary", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextPrimaryBrush", Brushes.Black) });
        stack.Children.Add(new TextBlock { Tag = "MediaAnalysisFindings", Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextSecondaryBrush", Brushes.DimGray) });

        var actions = new WrapPanel();
        var sync = Button("Synchronize media", async () => { await main.NavigateCommand.ExecuteAsync("WordPress Explorer"); await main.Explorer.SynchronizeNowAsync(); });
        sync.Tag = "MediaAnalysisSync";
        actions.Children.Add(sync);
        var generate = Button("Generate proposals", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Suggested Changes");
            if (main.SuggestedChanges.GenerateCommand.CanExecute(null)) await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
        });
        generate.Tag = "MediaAnalysisGenerate";
        actions.Children.Add(generate);
        var open = Button("Open media", async () => await main.NavigateCommand.ExecuteAsync("WordPress Explorer"));
        open.Tag = "MediaAnalysisOpen";
        actions.Children.Add(open);
        stack.Children.Add(actions);
        return shell;
    }

    private static void RefreshPanel(Border panel, MainWindowViewModel main)
    {
        panel.Visibility = main.CurrentPage is "WordPress Explorer" or "Suggested Changes" or "AI Studio" ? Visibility.Visible : Visibility.Collapsed;
        if (panel.Visibility != Visibility.Visible) return;

        var media = main.Explorer.Media.ToArray();
        var result = Analyze(media);
        SetText(panel, "MediaAnalysisSummary", media.Length == 0
            ? "No synchronized media is available yet."
            : $"Scanned {media.Length} media items • {result.TotalFindings} quality findings");
        SetText(panel, "MediaAnalysisFindings", media.Length == 0
            ? "Synchronize the selected website to cache its media library locally."
            : string.Join("\n",
                $"• Images missing alt text: {result.MissingAltText}",
                $"• Missing/untitled metadata: {result.MissingMetadata}",
                $"• Generic file names: {result.GenericFileNames}",
                $"• Large files over 500 KB: {result.LargeFiles}",
                $"• Very small images under 300 px: {result.SmallImages}",
                $"• Unknown dimensions or file size: {result.UnknownTechnicalMetadata}",
                $"• Duplicate URLs/file names: {result.Duplicates}",
                $"• Legacy image formats: {result.LegacyFormats}",
                $"• Invalid source URLs: {result.InvalidUrls}"));

        SetEnabled(panel, "MediaAnalysisSync", !main.IsOperationRunning);
        SetEnabled(panel, "MediaAnalysisGenerate", media.Length > 0 && !main.IsOperationRunning);
        SetEnabled(panel, "MediaAnalysisOpen", !main.IsOperationRunning);
    }

    private static MediaAnalysisResult Analyze(IReadOnlyCollection<WordPressMediaItem> media)
    {
        var images = media.Where(x => x.IsImage).ToArray();
        var missingAltText = images.Count(x => string.IsNullOrWhiteSpace(x.AltText));
        var missingMetadata = media.Count(x => string.IsNullOrWhiteSpace(x.Title) || string.IsNullOrWhiteSpace(x.Slug) || x.Title.Equals("Untitled", StringComparison.OrdinalIgnoreCase));
        var genericFileNames = media.Count(x => IsGenericFileName(string.IsNullOrWhiteSpace(x.FileName) ? x.SourceUrl : x.FileName));
        var largeFiles = images.Count(x => x.FileSizeBytes is > 512000);
        var smallImages = images.Count(x => (x.Width is > 0 and < 300) || (x.Height is > 0 and < 300));
        var unknownTechnicalMetadata = images.Count(x => x.Width is null || x.Height is null || x.FileSizeBytes is null);
        var duplicateUrls = media.Where(x => !string.IsNullOrWhiteSpace(x.SourceUrl)).GroupBy(x => x.SourceUrl.Trim(), StringComparer.OrdinalIgnoreCase).Sum(x => Math.Max(0, x.Count() - 1));
        var duplicateNames = media.Select(x => string.IsNullOrWhiteSpace(x.FileName) ? Path.GetFileName(x.SourceUrl) : x.FileName).Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Sum(x => Math.Max(0, x.Count() - 1));
        var legacyFormats = media.Count(x =>
        {
            var extension = Path.GetExtension(GetUrlPath(string.IsNullOrWhiteSpace(x.FileName) ? x.SourceUrl : x.FileName));
            return extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        });
        var invalidUrls = media.Count(x => !Uri.TryCreate(x.SourceUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps));
        return new MediaAnalysisResult(missingAltText, missingMetadata, genericFileNames, largeFiles, smallImages, unknownTechnicalMetadata, duplicateUrls + duplicateNames, legacyFormats, invalidUrls);
    }

    private static bool IsGenericFileName(string value)
    {
        var name = Path.GetFileNameWithoutExtension(GetUrlPath(value));
        if (string.IsNullOrWhiteSpace(name)) return true;
        var normalized = name.Trim().Trim('-', '_').ToLowerInvariant();
        if (GenericNames.Contains(normalized)) return true;
        return GenericNames.Any(prefix => normalized.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)) && normalized.Count(char.IsDigit) >= 3;
    }

    private static string GetUrlPath(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsolutePath : value ?? string.Empty;

    private static Button Button(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 7, 7), Padding = new Thickness(11, 7, 11, 7) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void SetText(DependencyObject root, string tag, string value) { var text = Find<TextBlock>(root, tag); if (text is not null) text.Text = value; }
    private static void SetEnabled(DependencyObject root, string tag, bool value) { var button = Find<Button>(root, tag); if (button is not null) button.IsEnabled = value; }
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

    private sealed record MediaAnalysisResult(int MissingAltText, int MissingMetadata, int GenericFileNames, int LargeFiles, int SmallImages, int UnknownTechnicalMetadata, int Duplicates, int LegacyFormats, int InvalidUrls)
    {
        public int TotalFindings => MissingAltText + MissingMetadata + GenericFileNames + LargeFiles + SmallImages + UnknownTechnicalMetadata + Duplicates + LegacyFormats + InvalidUrls;
    }
}
