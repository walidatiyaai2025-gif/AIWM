using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Provides an explicit, modal command workspace. Nothing opens automatically and
/// the owner window cannot receive clicks while the command center is visible.
/// </summary>
internal static class AiCommandAndBulkExecutionExperience
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
        if (window.DataContext is not MainWindowViewModel main) return;

        Attached.Add(window, new object());

        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Space || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            args.Handled = true;
            ShowCommandCenter(window, main);
        };

        window.Dispatcher.BeginInvoke(() => AddTitleBarButton(window, main));
    }

    private static void AddTitleBarButton(MainWindow window, MainWindowViewModel main)
    {
        if (window.FindName("AiCommandCenterButton") is not null) return;
        if (window.FindName("HelpModeButton") is not Button helpButton) return;
        if (VisualTreeHelper.GetParent(helpButton) is not StackPanel host) return;

        var button = new Button
        {
            Name = "AiCommandCenterButton",
            Content = "✦ AI Command",
            ToolTip = "Open AI Command Center (Ctrl+Space)",
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) => ShowCommandCenter(window, main);
        host.Children.Insert(0, button);
    }

    private static void ShowCommandCenter(MainWindow owner, MainWindowViewModel main)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "AI Command Center",
            Width = 720,
            Height = 620,
            MinWidth = 620,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = Brush("AppBackgroundBrush", Brushes.White)
        };

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dialog.Content = root;

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "AI Command Center",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Describe the result you need. The application prepares a safe plan before doing anything.",
            Margin = new Thickness(0, 6, 0, 16),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        root.Children.Add(heading);

        var commandBox = new TextBox
        {
            AcceptsReturn = true,
            Height = 82,
            Padding = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Examples: Synchronize the website; Run SEO audit; Generate suggestions; Execute all ready low-risk changes."
        };
        Grid.SetRow(commandBox, 1);
        root.Children.Add(commandBox);

        var commandActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 12)
        };
        var previewButton = Button("Preview plan");
        var executeButton = Button("Execute plan", primary: true);
        executeButton.IsEnabled = false;
        commandActions.Children.Add(previewButton);
        commandActions.Children.Add(executeButton);
        Grid.SetRow(commandActions, 2);
        root.Children.Add(commandActions);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        Grid.SetRow(content, 3);
        root.Children.Add(content);

        var preview = Card();
        var previewStack = new StackPanel();
        preview.Child = previewStack;
        previewStack.Children.Add(new TextBlock
        {
            Text = "COMMAND PLAN",
            FontWeight = FontWeights.Bold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        });
        var planText = new TextBlock
        {
            Text = "Enter a command, then select Preview plan.",
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimaryBrush", Brushes.Black)
        };
        previewStack.Children.Add(planText);
        content.Children.Add(preview);

        var bulk = Card();
        bulk.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(bulk, 1);
        content.Children.Add(bulk);
        var bulkStack = new StackPanel();
        bulk.Child = bulkStack;
        bulkStack.Children.Add(new TextBlock
        {
            Text = "SMART BULK EXECUTION",
            FontWeight = FontWeights.Bold,
            Foreground = Brush("PrimaryBrush", Brushes.Teal)
        });
        bulkStack.Children.Add(new TextBlock
        {
            Text = "These actions use the existing approval, backup, execution and verification pipeline.",
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        });
        bulkStack.Children.Add(BulkButton("Prepare all supported", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            if (main.ExecutionCenter.PrepareAllSupportedCommand.CanExecute(null))
                await main.ExecutionCenter.PrepareAllSupportedCommand.ExecuteAsync(null);
        }));
        bulkStack.Children.Add(BulkButton("Execute all ready", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            if (main.ExecutionCenter.ExecuteAllReadyCommand.CanExecute(null))
                await main.ExecutionCenter.ExecuteAllReadyCommand.ExecuteAsync(null);
        }, primary: true));
        bulkStack.Children.Add(BulkButton("Retry failed", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            if (main.ExecutionCenter.RetryFailedCommand.CanExecute(null))
                await main.ExecutionCenter.RetryFailedCommand.ExecuteAsync(null);
        }));
        bulkStack.Children.Add(BulkButton("Select ready items", async () =>
        {
            await main.NavigateCommand.ExecuteAsync("Execution Center");
            if (main.ExecutionCenter.SelectReadyCommand.CanExecute(null))
                main.ExecutionCenter.SelectReadyCommand.Execute(null);
        }));

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = "No WordPress write occurs during plan preview.",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextSecondaryBrush", Brushes.DimGray)
        };
        footer.Children.Add(hint);
        var close = Button("Close");
        close.Click += (_, _) => dialog.Close();
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        CommandPlan? currentPlan = null;
        previewButton.Click += (_, _) =>
        {
            currentPlan = Parse(commandBox.Text);
            planText.Text = currentPlan.Description;
            executeButton.IsEnabled = currentPlan.Action != CommandAction.Unknown;
        };
        executeButton.Click += async (_, _) =>
        {
            if (currentPlan is null || currentPlan.Action == CommandAction.Unknown) return;
            executeButton.IsEnabled = false;
            previewButton.IsEnabled = false;
            try
            {
                await ExecutePlanAsync(main, currentPlan);
                planText.Text = currentPlan.Description + "\n\n✓ Command handed to the application workflow.";
            }
            catch (Exception exception)
            {
                planText.Text = currentPlan.Description + $"\n\nFailed: {exception.Message}";
            }
            finally
            {
                previewButton.IsEnabled = true;
                executeButton.IsEnabled = true;
            }
        };

        commandBox.Focus();
        dialog.ShowDialog();
    }

    private static CommandPlan Parse(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return Unknown("Enter a command first.");

        if (ContainsAny(text, "sync", "synchronize", "synchronise", "مزامنة", "زامن"))
            return new(CommandAction.Synchronize, "1. Open WordPress Explorer.\n2. Synchronize the selected site.\n3. Preserve the previous offline snapshot if WordPress fails.");

        if (ContainsAny(text, "seo audit", "audit seo", "تحليل seo", "فحص seo", "تدقيق seo"))
            return new(CommandAction.RunSeoAudit, "1. Open SEO Audit.\n2. Run measurable local checks against the synchronized snapshot.\n3. Keep the results ready for proposal generation.");

        if (ContainsAny(text, "generate suggestion", "generate proposal", "اقتراح", "مقترحات", "تحسينات"))
            return new(CommandAction.GenerateSuggestions, "1. Open Suggested Changes.\n2. Generate reviewable proposals from local audit findings.\n3. Do not execute until approval.");

        if (ContainsAny(text, "execute all ready", "execute low risk", "نفذ الجاهز", "نفذ منخفض", "تنفيذ آمن"))
            return new(CommandAction.ExecuteAllReady, "1. Open Execution Center.\n2. Use only ready and supported actions.\n3. Run the existing backup, execution, verification and evidence pipeline.");

        if (ContainsAny(text, "retry failed", "اعد المحاولة", "إعادة المحاولة"))
            return new(CommandAction.RetryFailed, "1. Open Execution Center.\n2. Filter failed operations.\n3. Retry using the existing reliability policy.");

        if (ContainsAny(text, "review", "راجع", "مراجعة"))
            return new(CommandAction.ReviewSuggestions, "Open Suggested Changes and review the current proposal queue before approval.");

        if (ContainsAny(text, "approve", "اعتماد", "وافق"))
            return new(CommandAction.OpenApproval, "Open Approval Queue. No item is approved automatically by this command.");

        if (ContainsAny(text, "rollback", "تراجع", "استرجاع"))
            return new(CommandAction.OpenRollback, "Open Execution Center so you can select the exact executed item and review its rollback evidence.");

        if (ContainsAny(text, "backup", "نسخة احتياطية", "باك اب"))
            return new(CommandAction.OpenBackups, "Open Backup & Restore to create or verify a SQLite backup before high-impact work.");

        if (ContainsAny(text, "media", "alt text", "image", "صور", "وسائط"))
            return new(CommandAction.OpenMediaReview, "Open AI Studio for media quality, missing Alt Text and image-priority review.");

        if (ContainsAny(text, "health", "صحة الموقع", "حالة الموقع"))
            return new(CommandAction.OpenHealth, "Open Health Center to review the selected website's operational state.");

        return Unknown("The command is not recognized yet. Try: Synchronize website, Run SEO audit, Generate suggestions, Review changes, Execute all ready, Retry failed, Backup, Media review, or Site health.");
    }

    private static async Task ExecutePlanAsync(MainWindowViewModel main, CommandPlan plan)
    {
        switch (plan.Action)
        {
            case CommandAction.Synchronize:
                await main.NavigateCommand.ExecuteAsync("WordPress Explorer");
                if (main.Explorer.RefreshCommand.CanExecute(null))
                    await main.Explorer.RefreshCommand.ExecuteAsync(null);
                break;
            case CommandAction.RunSeoAudit:
                await main.NavigateCommand.ExecuteAsync("SEO Audit");
                if (main.SeoAudit.RunAuditCommand.CanExecute(null))
                    await main.SeoAudit.RunAuditCommand.ExecuteAsync(null);
                break;
            case CommandAction.GenerateSuggestions:
                await main.NavigateCommand.ExecuteAsync("Suggested Changes");
                if (main.SuggestedChanges.GenerateCommand.CanExecute(null))
                    await main.SuggestedChanges.GenerateCommand.ExecuteAsync(null);
                break;
            case CommandAction.ReviewSuggestions:
                await main.NavigateCommand.ExecuteAsync("Suggested Changes");
                break;
            case CommandAction.OpenApproval:
                await main.NavigateCommand.ExecuteAsync("Approval Queue");
                break;
            case CommandAction.ExecuteAllReady:
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                if (main.ExecutionCenter.ExecuteAllReadyCommand.CanExecute(null))
                    await main.ExecutionCenter.ExecuteAllReadyCommand.ExecuteAsync(null);
                break;
            case CommandAction.RetryFailed:
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                if (main.ExecutionCenter.RetryFailedCommand.CanExecute(null))
                    await main.ExecutionCenter.RetryFailedCommand.ExecuteAsync(null);
                break;
            case CommandAction.OpenRollback:
                await main.NavigateCommand.ExecuteAsync("Execution Center");
                break;
            case CommandAction.OpenBackups:
                await main.NavigateCommand.ExecuteAsync("Backups");
                break;
            case CommandAction.OpenMediaReview:
                await main.NavigateCommand.ExecuteAsync("AI Studio");
                break;
            case CommandAction.OpenHealth:
                await main.NavigateCommand.ExecuteAsync("Health Center");
                break;
        }
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static CommandPlan Unknown(string description) => new(CommandAction.Unknown, description);

    private static Border Card() => new()
    {
        Padding = new Thickness(18),
        CornerRadius = new CornerRadius(12),
        Background = Brush("SurfaceBrush", Brushes.White),
        BorderBrush = Brush("BorderBrush", Brushes.LightGray),
        BorderThickness = new Thickness(1)
    };

    private static Button Button(string text, bool primary = false) => new()
    {
        Content = text,
        MinWidth = 110,
        Padding = new Thickness(12, 6, 12, 6),
        Margin = new Thickness(6, 0, 0, 0),
        Background = primary ? Brush("PrimaryBrush", Brushes.Teal) : Brush("SurfaceAltBrush", Brushes.WhiteSmoke),
        Foreground = primary ? Brushes.White : Brush("TextPrimaryBrush", Brushes.Black)
    };

    private static Button BulkButton(string text, Func<Task> action, bool primary = false)
    {
        var button = Button(text, primary);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Margin = new Thickness(0, 0, 0, 8);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Brush Brush(string key, Brush fallback) =>
        global::System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private sealed record CommandPlan(CommandAction Action, string Description);

    private enum CommandAction
    {
        Unknown,
        Synchronize,
        RunSeoAudit,
        GenerateSuggestions,
        ReviewSuggestions,
        OpenApproval,
        ExecuteAllReady,
        RetryFailed,
        OpenRollback,
        OpenBackups,
        OpenMediaReview,
        OpenHealth
    }
}
