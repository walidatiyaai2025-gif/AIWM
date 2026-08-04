using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private string _journeyProfessionalStatus = "READY";
        private string _journeyProfessionalBlocker = "No blocker detected";
        private string _journeyProfessionalReason = "The next action is based on the latest offline workspace state.";
        private Brush _journeyProfessionalStatusBrush = Brushes.SeaGreen;

        public string JourneyProfessionalStatus
        {
            get => _journeyProfessionalStatus;
            private set => SetProperty(ref _journeyProfessionalStatus, value);
        }

        public string JourneyProfessionalBlocker
        {
            get => _journeyProfessionalBlocker;
            private set => SetProperty(ref _journeyProfessionalBlocker, value);
        }

        public string JourneyProfessionalReason
        {
            get => _journeyProfessionalReason;
            private set => SetProperty(ref _journeyProfessionalReason, value);
        }

        public Brush JourneyProfessionalStatusBrush
        {
            get => _journeyProfessionalStatusBrush;
            private set => SetProperty(ref _journeyProfessionalStatusBrush, value);
        }

        public string JourneyProfessionalVersion => "v1.3.11";

        partial void OnDashboardSeoScoreStateChanged(string value) => EvaluateProfessionalJourney();
        partial void OnDashboardJourneyProgressChanged(int value) => EvaluateProfessionalJourney();
        partial void OnDashboardFailedJobsChanged(int value) => EvaluateProfessionalJourney();
        partial void OnDashboardLastSiteSyncChanged(string value) => EvaluateProfessionalJourney();

        private void EvaluateProfessionalJourney()
        {
            if (Sites.SelectedSite is null)
            {
                JourneyProfessionalStatus = "ACTION REQUIRED";
                JourneyProfessionalBlocker = "No WordPress site is selected";
                JourneyProfessionalReason = "Add or select a site before synchronization, analysis, recommendations, or execution can begin.";
                JourneyProfessionalStatusBrush = Brushes.DarkOrange;
                CurrentJourneyStepTitle = "Select or add a WordPress site";
                CurrentJourneyStepDescription = "Create the active site context and validate its WordPress connection.";
                CurrentJourneyActionLabel = "Open Sites";
                CurrentJourneyTarget = "Sites";
                return;
            }

            if (DashboardLastSiteSync.Equals("Never synchronized", StringComparison.OrdinalIgnoreCase))
            {
                JourneyProfessionalStatus = "SYNC REQUIRED";
                JourneyProfessionalBlocker = "No synchronized WordPress snapshot is available";
                JourneyProfessionalReason = "The application must load a verified local snapshot before audits and AI recommendations are trusted.";
                JourneyProfessionalStatusBrush = Brushes.DodgerBlue;
                CurrentJourneyStepTitle = "Synchronize the active site";
                CurrentJourneyStepDescription = "Load WordPress content into SQLite while preserving the previous offline state.";
                CurrentJourneyActionLabel = "Open WordPress Explorer";
                CurrentJourneyTarget = "WordPress Explorer";
                return;
            }

            if (DashboardFailedJobs > 0)
            {
                JourneyProfessionalStatus = "ATTENTION";
                JourneyProfessionalBlocker = $"{DashboardFailedJobs} failed background job(s) require review";
                JourneyProfessionalReason = "Resolve or retry failed work before continuing, so the journey is based on complete and reliable data.";
                JourneyProfessionalStatusBrush = Brushes.IndianRed;
                CurrentJourneyStepTitle = "Resolve failed operations";
                CurrentJourneyStepDescription = "Review copyable errors, retry safe work, or open rollback where execution was interrupted.";
                CurrentJourneyActionLabel = "Open Jobs";
                CurrentJourneyTarget = "Jobs";
                return;
            }

            var safetyBlockers = new List<string>();
            if (!Settings.CaptureBeforeAfterEvidence) safetyBlockers.Add("before/after evidence");
            if (!Settings.RequireVerifiedExecutionResult) safetyBlockers.Add("post-write verification");
            if (!Settings.AutoRejectHighRiskAiActions) safetyBlockers.Add("high-risk automatic rejection");

            var executionStage = CurrentJourneyTarget.Equals("Execution Center", StringComparison.OrdinalIgnoreCase) ||
                                 CurrentJourneyTarget.Equals("Evidence Center", StringComparison.OrdinalIgnoreCase);
            if (executionStage && safetyBlockers.Count > 0)
            {
                JourneyProfessionalStatus = "SAFETY BLOCKED";
                JourneyProfessionalBlocker = $"Required controls are disabled: {string.Join(", ", safetyBlockers)}";
                JourneyProfessionalReason = "Execution remains blocked until backup evidence, verification, and high-risk rejection policies are enabled.";
                JourneyProfessionalStatusBrush = Brushes.IndianRed;
                CurrentJourneyStepTitle = "Enable execution safety controls";
                CurrentJourneyStepDescription = "Review AI Automation settings before any WordPress write is allowed.";
                CurrentJourneyActionLabel = "Open Settings";
                CurrentJourneyTarget = "Settings";
                return;
            }

            JourneyProfessionalStatus = DashboardJourneyProgress >= 100 ? "COMPLETE" : "READY";
            JourneyProfessionalBlocker = "No blocker detected";
            JourneyProfessionalReason = DashboardJourneyProgress >= 100
                ? "The verified journey is complete. Review evidence, results, history, and the next improvement opportunity."
                : "The recommended action is derived from the latest SQLite snapshot, audit results, approvals, jobs, and execution state.";
            JourneyProfessionalStatusBrush = DashboardJourneyProgress >= 100 ? Brushes.SeaGreen : Brushes.DodgerBlue;
        }
    }
}

namespace AIWordPressManager.Desktop
{
    public partial class MainWindow
    {
        static MainWindow()
        {
            EventManager.RegisterClassHandler(
                typeof(MainWindow),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(InstallProfessionalJourneyStatus));
        }

        private static void InstallProfessionalJourneyStatus(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;

            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var marker = FindTextBlock(window, "RECOMMENDED NEXT ACTION");
                if (marker?.Parent is not StackPanel markerPanel || markerPanel.Parent is not Grid actionGrid) return;
                if (actionGrid.FindName("ProfessionalJourneyStatus") is not null) return;

                if (actionGrid.RowDefinitions.Count == 0)
                    actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                actionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                foreach (UIElement child in actionGrid.Children)
                {
                    if (Grid.GetRow(child) == 0) continue;
                    Grid.SetRow(child, 0);
                }

                var statusBorder = new Border
                {
                    Name = "ProfessionalJourneyStatus",
                    Margin = new Thickness(0, 14, 0, 0),
                    Padding = new Thickness(12, 9, 12, 9),
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(1),
                    Background = (Brush)window.FindResource("SoftSurfaceBrush"),
                    BorderBrush = (Brush)window.FindResource("BorderBrush")
                };
                Grid.SetRow(statusBorder, 1);
                Grid.SetColumnSpan(statusBorder, 2);

                var statusGrid = new Grid();
                statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statusGrid.ColumnDefinitions.Add(new ColumnDefinition());

                var status = new TextBlock
                {
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                status.SetBinding(TextBlock.TextProperty, new Binding("JourneyProfessionalStatus"));
                status.SetBinding(TextBlock.ForegroundProperty, new Binding("JourneyProfessionalStatusBrush"));

                var details = new StackPanel();
                Grid.SetColumn(details, 1);
                var blocker = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                blocker.SetBinding(TextBlock.TextProperty, new Binding("JourneyProfessionalBlocker"));
                var reason = new TextBlock
                {
                    FontSize = 11,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)window.FindResource("TextSecondaryBrush")
                };
                reason.SetBinding(TextBlock.TextProperty, new Binding("JourneyProfessionalReason"));
                details.Children.Add(blocker);
                details.Children.Add(reason);

                statusGrid.Children.Add(status);
                statusGrid.Children.Add(details);
                statusBorder.Child = statusGrid;
                actionGrid.Children.Add(statusBorder);
            }));
        }

        private static TextBlock? FindTextBlock(DependencyObject parent, string text)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is TextBlock textBlock && textBlock.Text.Equals(text, StringComparison.Ordinal))
                    return textBlock;

                var nested = FindTextBlock(child, text);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
