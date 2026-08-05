using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

internal static class ExecutionTimelineExperience
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

        var page = BuildPage(main);
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle, window.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        var state = new State(window, main, page, timer);
        Attached.Add(window, state);

        Grid.SetRow(page, 3);
        Panel.SetZIndex(page, 30);
        root.Children.Add(page);

        timer.Tick += state.OnTimerTick;
        main.PropertyChanged += state.OnMainPropertyChanged;
        window.Activated += state.OnWindowActivated;
        window.Deactivated += state.OnWindowDeactivated;
        window.StateChanged += state.OnWindowStateChanged;
        window.Closed += state.OnWindowClosed;

        state.ApplyLifecycle();
    }

    private static Grid BuildPage(MainWindowViewModel main)
    {
        var page = new Grid
        {
            Background = Brush("AppBackgroundBrush", Brushes.White),
            Margin = new Thickness(24),
            Visibility = Visibility.Collapsed,
            Tag = "ExecutionTimelineWorkspace"
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Execution Timeline",
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("TextPrimaryBrush", Brushes.Black)
                },
                new TextBlock
                {
                    Text = "Real job history from SQLite with duration, progress, errors, retry and recovery routing.",
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
                }
            }
        });

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        headerActions.Children.Add(ActionButton("Refresh", async () =>
        {
            if (main.Jobs.RefreshCommand.CanExecute(null))
                await main.Jobs.RefreshCommand.ExecuteAsync(null);
        }));
        headerActions.Children.Add(ActionButton("Failed only", () =>
        {
            main.Jobs.ShowFailedCommand.Execute(null);
            return Task.CompletedTask;
        }));
        page.Children.Add(header);

        var summary = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(summary, 1);
        summary.Children.Add(SummaryCard("Running", nameof(JobsViewModel.RunningCount), "PrimaryBrush", main.Jobs));
        summary.Children.Add(SummaryCard("Waiting", nameof(JobsViewModel.WaitingCount), "TextPrimaryBrush", main.Jobs));
        summary.Children.Add(SummaryCard("Paused", nameof(JobsViewModel.PausedCount), "WarningBrush", main.Jobs));
        summary.Children.Add(SummaryCard("Completed", nameof(JobsViewModel.CompletedCount), "SuccessBrush", main.Jobs));
        summary.Children.Add(SummaryCard("Failed", nameof(JobsViewModel.FailedCount), "DangerBrush", main.Jobs));
        summary.Children.Add(SummaryCard("Cancelled", nameof(JobsViewModel.CancelledCount), "TextSecondaryBrush", main.Jobs));
        page.Children.Add(summary);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.75, GridUnitType.Star) });
        Grid.SetRow(workspace, 2);
        page.Children.Add(workspace);

        var tableBorder = Card();
        tableBorder.Margin = new Thickness(0, 0, 8, 0);
        workspace.Children.Add(tableBorder);
        var table = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            BorderThickness = new Thickness(0)
        };
        table.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(JobsViewModel.FilteredItems)) { Source = main.Jobs });
        table.SetBinding(DataGrid.SelectedItemProperty, new Binding(nameof(JobsViewModel.SelectedItem))
        {
            Source = main.Jobs,
            Mode = BindingMode.TwoWay
        });
        table.Columns.Add(TextColumn("Started UTC", nameof(JobRow.StartedAtUtc), 145, "{0:yyyy-MM-dd HH:mm:ss}"));
        table.Columns.Add(TextColumn("Site", nameof(JobRow.SiteName), 150));
        table.Columns.Add(TextColumn("Operation", nameof(JobRow.JobType), 140));
        table.Columns.Add(TextColumn("Status", nameof(JobRow.Status), 95));
        table.Columns.Add(TextColumn("Progress", nameof(JobRow.ProgressPercent), 85, "{0}%"));
        table.Columns.Add(TextColumn("Duration", nameof(JobRow.DurationText), 90));
        table.Columns.Add(TextColumn("Current step", nameof(JobRow.CurrentStep), 280));
        tableBorder.Child = table;

        var details = Card();
        details.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(details, 1);
        workspace.Children.Add(details);
        var detailStack = new StackPanel();
        details.Child = detailStack;
        detailStack.Children.Add(new TextBlock
        {
            Text = "Selected operation",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        detailStack.Children.Add(BoundLabel("Correlation ID", "SelectedItem.Id", main.Jobs));
        detailStack.Children.Add(BoundLabel("Status", "SelectedItem.Status", main.Jobs));
        detailStack.Children.Add(BoundLabel("Duration", "SelectedItem.DurationText", main.Jobs));
        detailStack.Children.Add(BoundLabel("Started UTC", "SelectedItem.StartedAtUtc", main.Jobs, "{0:yyyy-MM-dd HH:mm:ss}"));
        detailStack.Children.Add(BoundLabel("Completed UTC", "SelectedItem.CompletedAtUtc", main.Jobs, "{0:yyyy-MM-dd HH:mm:ss}"));
        detailStack.Children.Add(new TextBlock
        {
            Text = "Details / error",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 5)
        });
        var error = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        error.SetBinding(TextBox.TextProperty, new Binding("SelectedItem.ErrorDetails")
        {
            Source = main.Jobs,
            TargetNullValue = "No error details were recorded."
        });
        detailStack.Children.Add(error);

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        detailStack.Children.Add(actions);
        actions.Children.Add(ActionButton("Copy ID", () =>
        {
            if (main.Jobs.SelectedItem is { } job)
                Clipboard.SetText(job.Id.ToString());
            return Task.CompletedTask;
        }));
        actions.Children.Add(ActionButton("Copy details", () =>
        {
            if (main.Jobs.SelectedItem is { } job)
            {
                var summaryText = $"Correlation ID: {job.Id}\nSite: {job.SiteName}\nOperation: {job.JobType}\nStatus: {job.Status}\nProgress: {job.ProgressPercent}%\nStarted UTC: {job.StartedAtUtc:O}\nCompleted UTC: {job.CompletedAtUtc:O}\nDuration: {job.DurationText}\nStep: {job.CurrentStep}\nError: {job.ErrorDetails}";
                Clipboard.SetText(summaryText);
            }
            return Task.CompletedTask;
        }));
        actions.Children.Add(ActionButton("Retry", async () =>
        {
            if (main.Jobs.RetrySelectedCommand.CanExecute(null))
                await main.Jobs.RetrySelectedCommand.ExecuteAsync(null);
        }));
        actions.Children.Add(ActionButton("Recovery / rollback", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            if (main.ExecutionCenter.LoadCommand.CanExecute(null))
                await main.ExecutionCenter.LoadCommand.ExecuteAsync(null);
        }));

        var footer = new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 0),
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        footer.SetBinding(TextBlock.TextProperty, new Binding(nameof(JobsViewModel.QueueHealthText)) { Source = main.Jobs });
        Grid.SetRow(footer, 3);
        page.Children.Add(footer);
        return page;
    }

    private static Border SummaryCard(string label, string path, string brushKey, object source)
    {
        var card = Card();
        card.Margin = new Thickness(4);
        var stack = new StackPanel();
        card.Child = stack;
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("TextSecondaryBrush", Brushes.DimGray) });
        var value = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(brushKey, Brushes.Black)
        };
        value.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = source });
        stack.Children.Add(value);
        return card;
    }

    private static FrameworkElement BoundLabel(string label, string path, object source, string? stringFormat = null)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        var value = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        value.SetBinding(TextBlock.TextProperty, new Binding(path)
        {
            Source = source,
            StringFormat = stringFormat,
            TargetNullValue = "—"
        });
        stack.Children.Add(value);
        return stack;
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width, string? stringFormat = null) => new()
    {
        Header = header,
        Binding = new Binding(path) { StringFormat = stringFormat },
        Width = new DataGridLength(width)
    };

    private static Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 7, 7),
            Padding = new Thickness(11, 5, 11, 5),
            MinHeight = 27
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Border Card() => new()
    {
        Padding = new Thickness(14),
        CornerRadius = new CornerRadius(10),
        Background = Brush("SurfaceBrush", Brushes.White),
        BorderBrush = Brush("BorderBrush", Brushes.LightGray),
        BorderThickness = new Thickness(1)
    };

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed class State(MainWindow window, MainWindowViewModel main, Grid page, DispatcherTimer timer)
    {
        private bool _windowActive = window.IsActive;
        private bool _refreshBusy;

        public void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
                ApplyLifecycle();
        }

        public void OnWindowActivated(object? sender, EventArgs e)
        {
            _windowActive = true;
            ApplyLifecycle();
        }

        public void OnWindowDeactivated(object? sender, EventArgs e)
        {
            _windowActive = false;
            timer.Stop();
        }

        public void OnWindowStateChanged(object? sender, EventArgs e) => ApplyLifecycle();

        public async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_refreshBusy || !ShouldRun() || main.Jobs.IsBusy) return;
            _refreshBusy = true;
            try
            {
                await main.Jobs.LoadAsync();
            }
            finally
            {
                _refreshBusy = false;
            }
        }

        public void ApplyLifecycle()
        {
            var visible = main.CurrentPage.Equals("Activity Timeline", StringComparison.OrdinalIgnoreCase);
            page.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            page.IsHitTestVisible = visible;

            if (ShouldRun())
            {
                if (!timer.IsEnabled)
                    timer.Start();
            }
            else if (timer.IsEnabled)
            {
                timer.Stop();
            }
        }

        private bool ShouldRun() =>
            _windowActive &&
            window.WindowState != WindowState.Minimized &&
            main.CurrentPage.Equals("Activity Timeline", StringComparison.OrdinalIgnoreCase);

        public void OnWindowClosed(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= OnTimerTick;
            main.PropertyChanged -= OnMainPropertyChanged;
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnWindowClosed;
        }
    }
}
