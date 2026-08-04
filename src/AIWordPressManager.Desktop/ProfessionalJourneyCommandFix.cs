using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IAsyncRelayCommand? _professionalJourneyCommand;

    public IAsyncRelayCommand ProfessionalJourneyCommand =>
        _professionalJourneyCommand ??= new AsyncRelayCommand(
            ContinueProfessionalJourneyAsync,
            () => !IsGuidedAnalysisRunning && !IsSafeAutopilotRunning);

    private async Task ContinueProfessionalJourneyAsync()
    {
        if (Sites.SelectedSite is null)
        {
            await NavigateAsync("Sites");
            Sites.Wizard.Open();
            return;
        }

        if (DashboardLastSiteSync.Equals("Never synchronized", StringComparison.OrdinalIgnoreCase))
        {
            await NavigateAsync("WordPress Explorer");
            return;
        }

        if (DashboardFailedJobs > 0)
        {
            await NavigateAsync("Jobs");
            return;
        }

        if (CurrentJourneyTarget.Equals("Settings", StringComparison.OrdinalIgnoreCase))
        {
            await NavigateAsync("Settings");
            return;
        }

        if (DashboardSeoScoreState.Equals("NOT ANALYZED", StringComparison.OrdinalIgnoreCase))
        {
            await StartOptimizationAsync();
            return;
        }

        await NavigateAsync(CurrentJourneyTarget);
    }
}

namespace AIWordPressManager.Desktop;

internal static class ProfessionalJourneyCommandBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Install));
    }

    private static void Install(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window)) return;

        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var marker = FindTextBlock(window, "RECOMMENDED NEXT ACTION");
            if (marker?.Parent is not StackPanel markerPanel || markerPanel.Parent is not Grid actionGrid) return;

            var actionButton = actionGrid.Children
                .OfType<Button>()
                .FirstOrDefault(button => Grid.GetColumn(button) == 1);

            if (actionButton is null) return;

            BindingOperations.SetBinding(
                actionButton,
                Button.CommandProperty,
                new Binding("ProfessionalJourneyCommand"));
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
