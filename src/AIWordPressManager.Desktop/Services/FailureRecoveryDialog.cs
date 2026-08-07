using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AIWordPressManager.Desktop.Services;

public enum FailureRecoveryDecision
{
    Closed,
    Retried,
    Skipped,
    Paused,
    RolledBack
}

public sealed record FailureRecoveryRequest(
    Exception Exception,
    string Module,
    string UserMessage,
    string AppSolution,
    string? AiSolution = null,
    Func<CancellationToken, Task>? RetryAsync = null,
    Func<CancellationToken, Task>? SkipAsync = null,
    Func<CancellationToken, Task>? PauseAsync = null,
    Func<CancellationToken, Task>? RollbackAsync = null,
    string? EvidencePath = null);

public static class FailureRecoveryDialog
{
    public static Task<FailureRecoveryDecision> ShowAsync(
        FailureRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Exception);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return Task.FromResult(FailureRecoveryDecision.Closed);

        if (dispatcher.CheckAccess())
            return ShowCoreAsync(request, cancellationToken);

        return dispatcher.InvokeAsync(
            () => ShowCoreAsync(request, cancellationToken),
            DispatcherPriority.Normal).Task.Unwrap();
    }

    private static Task<FailureRecoveryDecision> ShowCoreAsync(
        FailureRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<FailureRecoveryDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var window = new Window
        {
            Title = "AI WordPress Manager — Recovery",
            Width = 820,
            Height = 650,
            MinWidth = 680,
            MinHeight = 520,
            Owner = CurrentOwner(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            Foreground = new SolidColorBrush(Color.FromRgb(236, 211, 132))
        };

        var status = new TextBlock
        {
            Text = "Choose the safest next action.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var actionButtons = new List<Button>();
        var isExecuting = false;

        async Task ExecuteAsync(
            FailureRecoveryDecision decision,
            Func<CancellationToken, Task> action,
            string runningText,
            string completedText)
        {
            if (isExecuting)
                return;

            isExecuting = true;
            SetButtonsEnabled(actionButtons, false);
            status.Text = runningText;

            try
            {
                await action(linkedCancellation.Token);
                status.Text = completedText;
                completion.TrySetResult(decision);
                window.Close();
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                status.Text = "The recovery action was cancelled.";
            }
            catch (Exception actionException)
            {
                status.Text = $"Recovery action failed: {actionException.Message}";
            }
            finally
            {
                isExecuting = false;
                if (window.IsLoaded)
                    SetButtonsEnabled(actionButtons, true);
            }
        }

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Operation needs recovery",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var message = new TextBlock
        {
            Text = request.UserMessage,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(message, 1);
        root.Children.Add(message);

        var solutionPanel = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(24, 31, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 86, 132)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = BuildSolutionText(request),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.White
            }
        };
        Grid.SetRow(solutionPanel, 2);
        root.Children.Add(solutionPanel);

        var details = new TextBox
        {
            Text = BuildDetails(request),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(27, 27, 27)),
            Foreground = new SolidColorBrush(Color.FromRgb(226, 195, 91)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 61, 23)),
            Padding = new Thickness(12),
            FontFamily = new FontFamily("Consolas")
        };
        Grid.SetRow(details, 3);
        root.Children.Add(details);

        Grid.SetRow(status, 4);
        root.Children.Add(status);

        var buttons = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        AddActionButton(buttons, actionButtons, "Copy details", () => CopySafely(details.Text, status));
        AddActionButton(buttons, actionButtons, "Copy solution", () => CopySafely(BuildSolutionText(request), status));

        if (!string.IsNullOrWhiteSpace(request.EvidencePath))
        {
            AddActionButton(buttons, actionButtons, "Open evidence", () =>
            {
                try
                {
                    var target = request.EvidencePath!;
                    if (File.Exists(target))
                        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    else if (Directory.Exists(target))
                        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
                    else
                        status.Text = "The evidence path no longer exists.";
                }
                catch (Exception openException)
                {
                    status.Text = $"Could not open evidence: {openException.Message}";
                }
            });
        }

        AddRecoveryButton(buttons, actionButtons, "Retry", request.RetryAsync,
            () => ExecuteAsync(FailureRecoveryDecision.Retried, request.RetryAsync!, "Retrying…", "Retry completed."));
        AddRecoveryButton(buttons, actionButtons, "Skip", request.SkipAsync,
            () => ExecuteAsync(FailureRecoveryDecision.Skipped, request.SkipAsync!, "Skipping…", "The item was skipped."));
        AddRecoveryButton(buttons, actionButtons, "Pause", request.PauseAsync,
            () => ExecuteAsync(FailureRecoveryDecision.Paused, request.PauseAsync!, "Pausing…", "The operation was paused."));
        AddRecoveryButton(buttons, actionButtons, "Rollback", request.RollbackAsync,
            () => ExecuteAsync(FailureRecoveryDecision.RolledBack, request.RollbackAsync!, "Rolling back…", "Rollback completed."));

        AddActionButton(buttons, actionButtons, "Close", window.Close);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        window.Content = root;
        window.Closed += (_, _) =>
        {
            linkedCancellation.Cancel();
            completion.TrySetResult(FailureRecoveryDecision.Closed);
        };

        window.Show();
        return completion.Task;
    }

    private static void AddRecoveryButton(
        Panel panel,
        ICollection<Button> buttons,
        string text,
        Func<CancellationToken, Task>? action,
        Func<Task> execute)
    {
        if (action is null)
            return;

        var button = CreateButton(text);
        button.Click += async (_, _) => await execute();
        buttons.Add(button);
        panel.Children.Add(button);
    }

    private static void AddActionButton(
        Panel panel,
        ICollection<Button> buttons,
        string text,
        Action action)
    {
        var button = CreateButton(text);
        button.Click += (_, _) => action();
        buttons.Add(button);
        panel.Children.Add(button);
    }

    private static Button CreateButton(string text) => new()
    {
        Content = text,
        Margin = new Thickness(8, 4, 0, 4),
        Padding = new Thickness(16, 8, 16, 8),
        MinWidth = 96
    };

    private static void SetButtonsEnabled(IEnumerable<Button> buttons, bool enabled)
    {
        foreach (var button in buttons)
            button.IsEnabled = enabled;
    }

    private static void CopySafely(string value, TextBlock status)
    {
        try
        {
            Clipboard.SetText(value);
            status.Text = "Copied to clipboard.";
        }
        catch (Exception clipboardException)
        {
            status.Text = $"Could not copy: {clipboardException.Message}";
        }
    }

    private static string BuildSolutionText(FailureRecoveryRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Application guidance");
        builder.AppendLine(request.AppSolution);

        if (!string.IsNullOrWhiteSpace(request.AiSolution))
        {
            builder.AppendLine();
            builder.AppendLine("AI guidance");
            builder.AppendLine(request.AiSolution);
        }

        return builder.ToString().Trim();
    }

    private static string BuildDetails(FailureRecoveryRequest request) => string.Join(
        Environment.NewLine,
        $"UTC: {DateTimeOffset.UtcNow:O}",
        $"Module: {request.Module}",
        $"Exception: {request.Exception.GetType().FullName}",
        $"Message: {request.Exception.Message}",
        string.Empty,
        request.Exception.ToString());

    private static Window? CurrentOwner() => Application.Current?.Windows
        .OfType<Window>()
        .FirstOrDefault(window => window.IsActive)
        ?? Application.Current?.MainWindow;
}
