using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class MainWindowViewModel
    {
        private IAsyncRelayCommand? _explainCurrentJourneyCommand;
        public IAsyncRelayCommand ExplainCurrentJourneyCommand =>
            _explainCurrentJourneyCommand ??= new AsyncRelayCommand(ExplainCurrentJourneyAsync);

        public string JourneyCompletedSummary => DashboardJourneyProgress switch
        {
            >= 100 => "All stages completed and verified",
            >= 85 => "Analysis, review, approval, execution and verification completed",
            >= 70 => "Analysis, review, preview, approval and execution completed",
            >= 55 => "Analysis, review, preview and approval completed",
            >= 40 => "Analysis, AI review and preview completed",
            >= 25 => "Baseline analysis and AI review completed",
            > 0 => "Baseline analysis completed",
            _ => Sites.SelectedSite is null ? "No website selected yet" : "Website selected; journey has not started"
        };

        public string JourneyRemainingSummary
        {
            get
            {
                var remaining = 7 - (int)Math.Round(DashboardJourneyProgress * 7d / 100d);
                return DashboardJourneyProgress >= 100
                    ? "No required stages remain. Continue with the next improvement cycle."
                    : $"Approximately {Math.Max(1, remaining)} guided stage(s) remain before verified completion.";
            }
        }

        private async Task ExplainCurrentJourneyAsync()
        {
            await _dialogService.ShowInformationAsync(
                "Why this is the next step",
                $"Current recommendation: {CurrentJourneyStepTitle}\n\n{CurrentJourneyStepDescription}\n\nWhy: {JourneyProfessionalReason}\n\nCompleted: {JourneyCompletedSummary}\n\nRemaining: {JourneyRemainingSummary}\n\nThe application uses the selected site, SQLite snapshot, audit results, approvals, failed jobs, execution state, and safety controls to choose this action.");
        }

        partial void OnDashboardJourneyProgressChanged(int value)
        {
            EvaluateProfessionalJourney();
            OnPropertyChanged(nameof(JourneyCompletedSummary));
            OnPropertyChanged(nameof(JourneyRemainingSummary));
        }
    }
}

namespace AIWordPressManager.Desktop
{
    internal static class HesitantUserGuidanceBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(Install));
        }

        private static void Install(object sender, RoutedEventArgs e)
        {
            if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var dashboardTitle = FindTextBlock(window, "AI WordPress Optimization Journey");
                if (dashboardTitle?.Parent is not StackPanel titlePanel || titlePanel.Parent is not Grid headerGrid) return;
                if (headerGrid.Children.OfType<Border>().Any(x => x.Tag?.ToString() == "HesitantUserGuide")) return;

                if (headerGrid.RowDefinitions.Count == 0)
                    headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                foreach (UIElement child in headerGrid.Children) Grid.SetRow(child, 0);

                var guide = new Border
                {
                    Tag = "HesitantUserGuide",
                    Margin = new Thickness(0, 16, 0, 0),
                    Padding = new Thickness(18),
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    BorderBrush = window.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
                    Background = window.TryFindResource("SurfaceAltBrush") as Brush ?? Brushes.Transparent
                };
                Grid.SetRow(guide, 1);
                Grid.SetColumnSpan(guide, 2);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                grid.Children.Add(CreateSection("✓  WHAT YOU COMPLETED", "JourneyCompletedSummary", Brushes.SeaGreen));
                var current = CreateSection("▶  WHAT TO DO NOW", "CurrentJourneyStepTitle", Brushes.DodgerBlue);
                Grid.SetColumn(current, 1); grid.Children.Add(current);
                var remaining = CreateSection("○  WHAT REMAINS", "JourneyRemainingSummary", Brushes.DarkOrange);
                Grid.SetColumn(remaining, 2); grid.Children.Add(remaining);

                var explain = new Button
                {
                    Content = "Why this step?",
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Explain the recommendation using the current site and workflow state."
                };
                if (window.TryFindResource("SecondaryButtonStyle") is Style style) explain.Style = style;
                explain.SetBinding(Button.CommandProperty, new Binding("ExplainCurrentJourneyCommand"));
                Grid.SetColumn(explain, 3); grid.Children.Add(explain);

                guide.Child = grid;
                headerGrid.Children.Add(guide);
            }));
        }

        private static Border CreateSection(string title, string bindingPath, Brush accent)
        {
            var border = new Border { Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(12), BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Brushes.Gray };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = accent });
            var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), FontWeight = FontWeights.SemiBold };
            detail.SetBinding(TextBlock.TextProperty, new Binding(bindingPath));
            stack.Children.Add(detail); border.Child = stack; return border;
        }

        private static TextBlock? FindTextBlock(DependencyObject parent, string text)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is TextBlock block && block.Text.Equals(text, StringComparison.Ordinal)) return block;
                var nested = FindTextBlock(child, text);
                if (nested is not null) return nested;
            }
            return null;
        }
    }
}
