using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        public ObservableCollection<CompleteJourneyStep> CompleteJourneySteps { get; } = [];

        private string _completeJourneyHeadline = "Connect your first WordPress site";
        private string _completeJourneySummary = "Follow one controlled path from site registration to verified WordPress execution.";
        private string _completeJourneyCompletedText = "0 of 9 stages completed";
        private string _completeJourneyRemainingText = "9 stages remaining";
        private int _completeJourneyPercent;

        public string CompleteJourneyHeadline
        {
            get => _completeJourneyHeadline;
            private set => SetProperty(ref _completeJourneyHeadline, value);
        }

        public string CompleteJourneySummary
        {
            get => _completeJourneySummary;
            private set => SetProperty(ref _completeJourneySummary, value);
        }

        public string CompleteJourneyCompletedText
        {
            get => _completeJourneyCompletedText;
            private set => SetProperty(ref _completeJourneyCompletedText, value);
        }

        public string CompleteJourneyRemainingText
        {
            get => _completeJourneyRemainingText;
            private set => SetProperty(ref _completeJourneyRemainingText, value);
        }

        public int CompleteJourneyPercent
        {
            get => _completeJourneyPercent;
            private set => SetProperty(ref _completeJourneyPercent, value);
        }

        internal void RefreshCompleteUserJourney()
        {
            var hasSite = Sites.SelectedSite is not null;
            var hasSync = hasSite && !DashboardLastSiteSync.Equals("Never synchronized", StringComparison.OrdinalIgnoreCase);
            var hasAnalysis = hasSync && !DashboardSeoScoreState.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase);
            var hasRecommendations = hasAnalysis && DashboardAiSuggestions > 0;
            var hasPreview = IsCompletedState(JourneyPreviewState);
            var hasApproval = IsCompletedState(JourneyApprovalState);
            var hasExecution = IsCompletedState(JourneyExecuteState);
            var hasVerification = IsCompletedState(JourneyVerifyState);
            var isComplete = IsCompletedState(JourneyDoneState) || DashboardJourneyProgress >= 100;

            var states = new[]
            {
                new JourneyDefinition("1", "Register website", "Add the WordPress URL and application credentials, test the connection, then save the site.", "Sites", hasSite),
                new JourneyDefinition("2", "Synchronize WordPress", "Read posts, pages, media, categories, tags, theme and plugin information into SQLite.", "WordPress Explorer", hasSync),
                new JourneyDefinition("3", "Review synchronized data", "Confirm the local snapshot, totals, active theme and connection state before analysis.", "WordPress Explorer", hasSync),
                new JourneyDefinition("4", "Analyze website", "Create the SEO, content, links, accessibility and technical baseline.", "SEO Audit", hasAnalysis),
                new JourneyDefinition("5", "Review recommendations", "Review AI and application suggestions, impact, risk and expected result.", "Suggested Changes", hasRecommendations),
                new JourneyDefinition("6", "Preview proposed changes", "Compare current and proposed values before anything is written to WordPress.", "Suggested Changes", hasPreview),
                new JourneyDefinition("7", "Approve safe changes", "Select the changes that are allowed to enter the execution queue.", "Approval Queue", hasApproval),
                new JourneyDefinition("8", "Execute on WordPress", "Create backup evidence, write approved changes and preserve the audit trail.", "Execution Center", hasExecution),
                new JourneyDefinition("9", "Verify and finish", "Read the updated WordPress values, compare before and after, and keep rollback evidence.", "Evidence Center", hasVerification || isComplete)
            };

            var currentIndex = Array.FindIndex(states, item => !item.Completed);
            if (currentIndex < 0) currentIndex = states.Length - 1;

            CompleteJourneySteps.Clear();
            for (var index = 0; index < states.Length; index++)
            {
                var definition = states[index];
                var status = definition.Completed ? "DONE" : index == currentIndex ? "CURRENT" : "WAITING";
                CompleteJourneySteps.Add(new CompleteJourneyStep(
                    definition.Number,
                    definition.Title,
                    definition.Description,
                    definition.Target,
                    status,
                    definition.Completed,
                    index == currentIndex));
            }

            var completed = states.Count(item => item.Completed);
            CompleteJourneyPercent = (int)Math.Round(completed * 100d / states.Length);
            CompleteJourneyCompletedText = $"{completed} of {states.Length} stages completed";
            CompleteJourneyRemainingText = completed == states.Length
                ? "The complete WordPress journey is verified"
                : $"{states.Length - completed} stage(s) remaining";

            var current = states[currentIndex];
            CompleteJourneyHeadline = current.Completed ? "WordPress journey completed" : current.Title;
            CompleteJourneySummary = current.Completed
                ? "All required stages are complete. Review evidence, results and the next optimization opportunity."
                : current.Description;

            if (!HasScheduledSyncPause)
            {
                CurrentJourneyStepTitle = current.Title;
                CurrentJourneyStepDescription = current.Description;
                CurrentJourneyActionLabel = current.Completed ? "Open Evidence" : BuildActionLabel(current.Target);
                CurrentJourneyTarget = current.Target;
            }
        }

        private static bool IsCompletedState(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            (value.Equals("DONE", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("VERIFIED", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase));

        private static string BuildActionLabel(string target) => target switch
        {
            "Sites" => "Add or select site",
            "WordPress Explorer" => "Open synchronization",
            "SEO Audit" => "Start analysis",
            "Suggested Changes" => "Review suggestions",
            "Approval Queue" => "Open approval queue",
            "Execution Center" => "Open execution center",
            "Evidence Center" => "Review verification",
            _ => "Continue"
        };

        private sealed record JourneyDefinition(
            string Number,
            string Title,
            string Description,
            string Target,
            bool Completed);
    }

    public sealed record CompleteJourneyStep(
        string Number,
        string Title,
        string Description,
        string Target,
        string Status,
        bool IsCompleted,
        bool IsCurrent)
    {
        public Brush StatusBrush => IsCompleted ? Brushes.SeaGreen : IsCurrent ? Brushes.DodgerBlue : Brushes.SlateGray;
        public string StatusIcon => IsCompleted ? "✓" : IsCurrent ? "▶" : "○";
    }
}

namespace AIWordPressManager.Desktop
{
    internal static class CompleteUserJourneyBootstrap
    {
        private static readonly DispatcherTimer RefreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private static WeakReference<MainWindow>? _windowReference;

        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded),
                true);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
            _windowReference = new WeakReference<MainWindow>(window);

            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                InstallJourneyCenter(window);
                Refresh(window);
            }));

            RefreshTimer.Stop();
            RefreshTimer.Tick -= RefreshTimerOnTick;
            RefreshTimer.Tick += RefreshTimerOnTick;
            RefreshTimer.Start();
        }

        private static void RefreshTimerOnTick(object? sender, EventArgs e)
        {
            if (_windowReference is null || !_windowReference.TryGetTarget(out var window) || !window.IsLoaded)
            {
                RefreshTimer.Stop();
                return;
            }
            Refresh(window);
        }

        private static void Refresh(MainWindow window)
        {
            if (window.DataContext is ViewModels.MainWindowViewModel viewModel)
                viewModel.RefreshCompleteUserJourney();
        }

        private static void InstallJourneyCenter(MainWindow window)
        {
            var marker = FindTextBlock(window, "Guided optimization workflow");
            if (marker?.Parent is not StackPanel headerPanel || headerPanel.Children.OfType<Border>().Any(x => Equals(x.Tag, "CompleteJourneyCenter")))
                return;

            var center = new Border
            {
                Tag = "CompleteJourneyCenter",
                Margin = new Thickness(0, 14, 0, 14),
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                BorderBrush = ResolveBrush(window, "BorderBrush", Brushes.LightGray),
                Background = ResolveBrush(window, "SurfaceAltBrush", Brushes.WhiteSmoke)
            };

            var root = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titlePanel = new StackPanel();
            titlePanel.Children.Add(new TextBlock
            {
                Text = "COMPLETE USER JOURNEY",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = ResolveBrush(window, "PrimaryBrush", Brushes.DodgerBlue)
            });
            var headline = new TextBlock { FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 0) };
            headline.SetBinding(TextBlock.TextProperty, new Binding("CompleteJourneyHeadline"));
            var summary = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 18, 0), Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.DimGray) };
            summary.SetBinding(TextBlock.TextProperty, new Binding("CompleteJourneySummary"));
            titlePanel.Children.Add(headline);
            titlePanel.Children.Add(summary);
            header.Children.Add(titlePanel);

            var continueButton = new Button
            {
                MinWidth = 150,
                Padding = new Thickness(15, 9, 15, 9),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(continueButton, 1);
            continueButton.SetBinding(Button.ContentProperty, new Binding("CurrentJourneyActionLabel"));
            continueButton.SetBinding(Button.CommandProperty, new Binding("ContinueJourneyCommand"));
            header.Children.Add(continueButton);
            root.Children.Add(header);

            var progress = new ProgressBar { Height = 8, Maximum = 100, Margin = new Thickness(0, 15, 0, 7) };
            progress.SetBinding(ProgressBar.ValueProperty, new Binding("CompleteJourneyPercent"));
            root.Children.Add(progress);

            var metrics = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            metrics.ColumnDefinitions.Add(new ColumnDefinition());
            var completedText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
            completedText.SetBinding(TextBlock.TextProperty, new Binding("CompleteJourneyCompletedText"));
            var remainingText = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = ResolveBrush(window, "TextSecondaryBrush", Brushes.DimGray) };
            remainingText.SetBinding(TextBlock.TextProperty, new Binding("CompleteJourneyRemainingText"));
            Grid.SetColumn(remainingText, 1);
            metrics.Children.Add(completedText);
            metrics.Children.Add(remainingText);
            root.Children.Add(metrics);

            var items = new ItemsControl();
            items.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CompleteJourneySteps"));
            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 5));
            factory.SetValue(Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            factory.SetValue(Border.BorderBrushProperty, ResolveBrush(window, "BorderBrush", Brushes.LightGray));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            gridFactory.AppendChild(CreateJourneyTextFactory());
            factory.AppendChild(gridFactory);
            items.ItemTemplate = new DataTemplate { VisualTree = factory };
            root.Children.Add(items);

            center.Child = root;
            headerPanel.Children.Add(center);
        }

        private static FrameworkElementFactory CreateJourneyTextFactory()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new Binding("StatusIcon"));
            icon.SetBinding(TextBlock.ForegroundProperty, new Binding("StatusBrush"));
            icon.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            icon.SetValue(TextBlock.WidthProperty, 26d);
            icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(icon);

            var textPanel = new FrameworkElementFactory(typeof(StackPanel));
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            var description = new FrameworkElementFactory(typeof(TextBlock));
            description.SetBinding(TextBlock.TextProperty, new Binding("Description"));
            description.SetValue(TextBlock.FontSizeProperty, 10d);
            description.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            description.SetValue(TextBlock.OpacityProperty, 0.72d);
            description.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 0));
            textPanel.AppendChild(title);
            textPanel.AppendChild(description);
            panel.AppendChild(textPanel);
            return panel;
        }

        private static Brush ResolveBrush(FrameworkElement element, string key, Brush fallback)
            => element.TryFindResource(key) is Brush brush ? brush : fallback;

        private static TextBlock? FindTextBlock(DependencyObject parent, string text)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is TextBlock block && string.Equals(block.Text, text, StringComparison.Ordinal)) return block;
                var nested = FindTextBlock(child, text);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
