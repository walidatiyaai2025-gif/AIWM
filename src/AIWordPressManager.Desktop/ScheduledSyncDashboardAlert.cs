using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private bool _hasScheduledSyncPause;
        private string _scheduledSyncPauseTitle = "Scheduled synchronization is healthy";
        private string _scheduledSyncPauseDetail = "No paused WordPress synchronization was detected.";
        private string _scheduledSyncPauseRetryText = string.Empty;
        private DateTimeOffset? _scheduledSyncPauseRetryAt;

        public bool HasScheduledSyncPause
        {
            get => _hasScheduledSyncPause;
            private set => SetProperty(ref _hasScheduledSyncPause, value);
        }

        public string ScheduledSyncPauseTitle
        {
            get => _scheduledSyncPauseTitle;
            private set => SetProperty(ref _scheduledSyncPauseTitle, value);
        }

        public string ScheduledSyncPauseDetail
        {
            get => _scheduledSyncPauseDetail;
            private set => SetProperty(ref _scheduledSyncPauseDetail, value);
        }

        public string ScheduledSyncPauseRetryText
        {
            get => _scheduledSyncPauseRetryText;
            private set => SetProperty(ref _scheduledSyncPauseRetryText, value);
        }

        internal void ApplyScheduledSyncPause(ScheduledSyncPauseInfo? pause)
        {
            if (pause is null || pause.RetryAt <= DateTimeOffset.Now)
            {
                var wasPaused = HasScheduledSyncPause;
                HasScheduledSyncPause = false;
                ScheduledSyncPauseTitle = "Scheduled synchronization is healthy";
                ScheduledSyncPauseDetail = "No paused WordPress synchronization was detected.";
                ScheduledSyncPauseRetryText = string.Empty;
                _scheduledSyncPauseRetryAt = null;
                if (wasPaused) EvaluateProfessionalJourney();
                return;
            }

            HasScheduledSyncPause = true;
            _scheduledSyncPauseRetryAt = pause.RetryAt;
            ScheduledSyncPauseTitle = "Scheduled WordPress synchronization is temporarily paused";
            ScheduledSyncPauseDetail = $"Site {pause.SiteId} reached {pause.FailureCount} consecutive failures. Repeated automatic requests are paused for protection.";
            UpdateScheduledSyncCountdown();

            JourneyProfessionalStatus = "SYNC PAUSED";
            JourneyProfessionalBlocker = $"WordPress synchronization paused after {pause.FailureCount} consecutive failures";
            JourneyProfessionalReason = $"The next automatic retry is scheduled for {pause.RetryAt.LocalDateTime:g}. Review the failed job or wait for the protected retry window.";
            JourneyProfessionalStatusBrush = Brushes.DarkOrange;
            CurrentJourneyStepTitle = "Review the paused synchronization";
            CurrentJourneyStepDescription = "Inspect the failure, correct credentials or connectivity, then retry safely from Jobs.";
            CurrentJourneyActionLabel = "Open Jobs";
            CurrentJourneyTarget = "Jobs";
        }

        internal void UpdateScheduledSyncCountdown()
        {
            if (!HasScheduledSyncPause || _scheduledSyncPauseRetryAt is null) return;
            var remaining = _scheduledSyncPauseRetryAt.Value - DateTimeOffset.Now;
            if (remaining <= TimeSpan.Zero)
            {
                ApplyScheduledSyncPause(null);
                return;
            }

            ScheduledSyncPauseRetryText = $"Next automatic retry in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} minute(s) — {_scheduledSyncPauseRetryAt.Value.LocalDateTime:g}";
        }
    }
}

namespace AIWordPressManager.Desktop
{
    internal sealed record ScheduledSyncPauseInfo(string SiteId, int FailureCount, DateTimeOffset RetryAt);

    internal static class ScheduledSyncDashboardAlert
    {
        private static readonly Regex PauseRegex = new(
            @"Scheduled synchronization failed for (?<site>[0-9a-fA-F-]{36}): WordPressSync is paused after (?<failures>\d+) consecutive failures\. Try again in (?<minutes>\d+) minute\(s\), at (?<retry>.+?)\.",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly DispatcherTimer CountdownTimer = new()
        {
            Interval = TimeSpan.FromMinutes(1)
        };

        private static readonly List<FileSystemWatcher> Watchers = new();
        private static WeakReference<MainWindow>? _windowReference;
        private static Border? _alertBorder;
        private static string? _lastLogPath;
        private static DateTime _lastLogWriteUtc;
        private static bool _refreshQueued;

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnMainWindowLoaded), true);
        }

        private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
            _windowReference = new WeakReference<MainWindow>(window);

            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                InstallAlert(window);
                InstallLogWatchers(window);
                RefreshAlert(window);
            }));

            CountdownTimer.Stop();
            CountdownTimer.Tick -= CountdownTimerOnTick;
            CountdownTimer.Tick += CountdownTimerOnTick;

            window.Closed += (_, _) =>
            {
                CountdownTimer.Stop();
                DisposeWatchers();
                _windowReference = null;
                _alertBorder = null;
            };
        }

        private static void CountdownTimerOnTick(object? sender, EventArgs e)
        {
            if (_windowReference is null || !_windowReference.TryGetTarget(out var window) || !window.IsLoaded)
            {
                CountdownTimer.Stop();
                return;
            }

            if (window.DataContext is not ViewModels.MainWindowViewModel viewModel || !viewModel.HasScheduledSyncPause)
            {
                CountdownTimer.Stop();
                return;
            }

            viewModel.UpdateScheduledSyncCountdown();
            if (_alertBorder is not null)
                _alertBorder.Visibility = viewModel.HasScheduledSyncPause ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void InstallLogWatchers(MainWindow window)
        {
            DisposeWatchers();
            foreach (var directory in GetLogDirectories().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var watcher = new FileSystemWatcher(directory, "*.log")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false,
                        EnableRaisingEvents = true
                    };
                    watcher.Created += (_, _) => QueueRefresh(window);
                    watcher.Changed += (_, _) => QueueRefresh(window);
                    watcher.Renamed += (_, _) => QueueRefresh(window);
                    watcher.Deleted += (_, _) => QueueRefresh(window);
                    Watchers.Add(watcher);
                }
                catch
                {
                    // Diagnostics must not interrupt startup when a log folder cannot be watched.
                }
            }
        }

        private static void QueueRefresh(MainWindow window)
        {
            if (_refreshQueued || !window.IsLoaded) return;
            _refreshQueued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                if (window.IsLoaded) RefreshAlert(window);
            }));
        }

        private static void DisposeWatchers()
        {
            foreach (var watcher in Watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
            Watchers.Clear();
        }

        private static void InstallAlert(MainWindow window)
        {
            if (_alertBorder is not null) return;
            var marker = FindTextBlock(window, "Guided optimization workflow");
            if (marker?.Parent is not StackPanel headerPanel) return;

            _alertBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(14, 11, 14, 11),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Background = ResolveBrush(window, "WarningSoftBrush", new SolidColorBrush(Color.FromRgb(255, 247, 237))),
                BorderBrush = ResolveBrush(window, "WarningBrush", Brushes.DarkOrange),
                Visibility = Visibility.Collapsed
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            grid.Children.Add(new TextBlock
            {
                Text = "!", FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkOrange, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var textPanel = new StackPanel();
            Grid.SetColumn(textPanel, 1);
            var title = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            title.SetBinding(TextBlock.TextProperty, new Binding("ScheduledSyncPauseTitle"));
            var detail = new TextBlock { Margin = new Thickness(0, 3, 10, 0), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            detail.SetBinding(TextBlock.TextProperty, new Binding("ScheduledSyncPauseDetail"));
            var retry = new TextBlock { Margin = new Thickness(0, 4, 10, 0), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
            retry.SetBinding(TextBlock.TextProperty, new Binding("ScheduledSyncPauseRetryText"));
            textPanel.Children.Add(title);
            textPanel.Children.Add(detail);
            textPanel.Children.Add(retry);
            grid.Children.Add(textPanel);

            var openJobs = new Button
            {
                Content = "Open Jobs", MinWidth = 105, Padding = new Thickness(12, 7, 12, 7),
                VerticalAlignment = VerticalAlignment.Center, CommandParameter = "Jobs"
            };
            Grid.SetColumn(openJobs, 2);
            openJobs.SetBinding(Button.CommandProperty, new Binding("NavigateCommand"));
            grid.Children.Add(openJobs);

            _alertBorder.Child = grid;
            headerPanel.Children.Add(_alertBorder);
        }

        private static void RefreshAlert(MainWindow window)
        {
            if (window.DataContext is not ViewModels.MainWindowViewModel viewModel || _alertBorder is null) return;
            var latestLog = FindLatestLog();
            if (latestLog is null)
            {
                viewModel.ApplyScheduledSyncPause(null);
                _alertBorder.Visibility = Visibility.Collapsed;
                CountdownTimer.Stop();
                return;
            }

            var writeUtc = File.GetLastWriteTimeUtc(latestLog);
            if (!string.Equals(_lastLogPath, latestLog, StringComparison.OrdinalIgnoreCase) || writeUtc != _lastLogWriteUtc)
            {
                _lastLogPath = latestLog;
                _lastLogWriteUtc = writeUtc;
                viewModel.ApplyScheduledSyncPause(ReadLatestPause(latestLog));
            }

            _alertBorder.Visibility = viewModel.HasScheduledSyncPause ? Visibility.Visible : Visibility.Collapsed;
            if (viewModel.HasScheduledSyncPause)
            {
                viewModel.UpdateScheduledSyncCountdown();
                if (!CountdownTimer.IsEnabled) CountdownTimer.Start();
            }
            else
            {
                CountdownTimer.Stop();
            }
        }

        private static ScheduledSyncPauseInfo? ReadLatestPause(string path)
        {
            try
            {
                var lines = ReadTailLines(path, 600);
                for (var index = lines.Count - 1; index >= 0; index--)
                {
                    var match = PauseRegex.Match(lines[index]);
                    if (!match.Success) continue;
                    var failures = int.Parse(match.Groups["failures"].Value, CultureInfo.InvariantCulture);
                    var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                    var retryAt = DateTimeOffset.Now.AddMinutes(minutes);
                    if (DateTimeOffset.TryParse(match.Groups["retry"].Value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
                        retryAt = parsed;
                    return new ScheduledSyncPauseInfo(match.Groups["site"].Value, failures, retryAt);
                }
            }
            catch
            {
                // Dashboard diagnostics must never interrupt startup or navigation.
            }
            return null;
        }

        private static List<string> ReadTailLines(string path, int maximumLines)
        {
            var queue = new Queue<string>(maximumLines);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == maximumLines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return [.. queue];
        }

        private static string? FindLatestLog()
        {
            return GetLogDirectories().Where(Directory.Exists)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static IEnumerable<string> GetLogDirectories()
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIWordPressManager", "Logs");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI WordPress Manager", "Logs");
            yield return Path.Combine(AppContext.BaseDirectory, "Logs");
            yield return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
            => element.TryFindResource(key) is Brush brush ? brush : fallback;

        private static TextBlock? FindTextBlock(DependencyObject parent, string text)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is TextBlock textBlock && string.Equals(textBlock.Text, text, StringComparison.Ordinal)) return textBlock;
                var nested = FindTextBlock(child, text);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
