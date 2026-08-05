using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIWordPressManager.Application.Changes;
using Microsoft.Extensions.Logging;

namespace AIWordPressManager.Desktop.Services;

public sealed class GlobalErrorPresenter(ILogger<GlobalErrorPresenter> logger, AiErrorAdvisorService aiAdvisor)
{
    private static readonly object WindowGate = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> RecentErrors = new(StringComparer.Ordinal);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(15);
    private static WeakReference<Window>? _activeWindow;

    public void Show(Exception exception, string module = "Application")
    {
        ArgumentNullException.ThrowIfNull(exception);

        var key = BuildErrorKey(exception, module);
        var now = DateTimeOffset.UtcNow;
        if (RecentErrors.TryGetValue(key, out var lastShown) && now - lastShown < DuplicateWindow)
        {
            logger.LogWarning(exception, "Repeated error suppressed in {Module}", module);
            ActivateExistingWindow();
            return;
        }

        RecentErrors[key] = now;
        TrimOldErrors(now);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            logger.LogError(exception, "Unhandled error in {Module}; UI dispatcher is unavailable", module);
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => ShowCoreOrActivate(exception, module)));
            return;
        }

        ShowCoreOrActivate(exception, module);
    }

    private void ShowCoreOrActivate(Exception exception, string module)
    {
        lock (WindowGate)
        {
            if (_activeWindow is not null &&
                _activeWindow.TryGetTarget(out var existing) &&
                existing.IsLoaded && existing.IsVisible)
            {
                existing.Activate();
                existing.Topmost = true;
                existing.Topmost = false;
                return;
            }
        }

        ShowCore(exception, module);
    }

    private void ShowCore(Exception exception, string module)
    {
        var correlationId = $"ERR-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..29].ToUpperInvariant();
        logger.LogError(exception, "Unhandled error {CorrelationId} in {Module}", correlationId, module);

        var friendly = exception is AiProviderException ai ? ai.UserMessage : GetFriendlyMessage(exception);
        var details = Redact(BuildDetails(exception, module, correlationId));
        using var diagnosisCancellation = new CancellationTokenSource();

        var window = new Window
        {
            Title = "AI WordPress Manager — Error",
            Width = 760,
            Height = 560,
            MinWidth = 620,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = CurrentMainWindow(),
            Background = new SolidColorBrush(Color.FromRgb(15, 15, 15)),
            Foreground = new SolidColorBrush(Color.FromRgb(231, 204, 120)),
            ShowInTaskbar = false
        };

        lock (WindowGate)
            _activeWindow = new WeakReference<Window>(window);

        window.Closed += (_, _) =>
        {
            diagnosisCancellation.Cancel();
            lock (WindowGate)
            {
                if (_activeWindow is not null &&
                    _activeWindow.TryGetTarget(out var current) &&
                    ReferenceEquals(current, window))
                {
                    _activeWindow = null;
                }
            }
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Something went wrong",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var summary = new TextBlock
        {
            Text = friendly,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(title, 0);
        Grid.SetRow(summary, 1);
        root.Children.Add(title);
        root.Children.Add(summary);

        var text = new TextBox
        {
            Text = details,
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
        Grid.SetRow(text, 2);
        root.Children.Add(text);

        var aiPanel = new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(24, 31, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 86, 132)),
            BorderThickness = new Thickness(1)
        };
        var aiText = new TextBlock
        {
            Text = "AI is analyzing the error and preparing an exact action…",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        };
        aiPanel.Child = aiText;
        Grid.SetRow(aiPanel, 3);
        root.Children.Add(aiPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.Children.Add(CreateButton("Copy full details", () => Clipboard.SetText(details)));
        buttons.Children.Add(CreateButton("Copy AI solution", () =>
        {
            if (!string.IsNullOrWhiteSpace(aiText.Text))
                Clipboard.SetText(aiText.Text);
        }));
        buttons.Children.Add(CreateButton("Open logs folder", () =>
        {
            try
            {
                var folder = GetLogsFolder();
                System.IO.Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
            catch
            {
            }
        }));
        buttons.Children.Add(CreateButton("Close", window.Close));
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);
        window.Content = root;

        window.Loaded += async (_, _) =>
        {
            try
            {
                var advice = await aiAdvisor.AnalyzeAsync(exception, module);
                diagnosisCancellation.Token.ThrowIfCancellationRequested();
                aiText.Text = advice is null
                    ? "AI diagnosis is disabled in Settings → AI Automation."
                    : string.Join(
                        Environment.NewLine,
                        "AI diagnosis",
                        advice.Diagnosis,
                        string.Empty,
                        "Exact action",
                        advice.ExactAction,
                        string.Empty,
                        $"Risk: {advice.RiskLevel} • Decision: {advice.Decision}");

                AppendAiResolutionLog(correlationId, module, friendly, aiText.Text);
            }
            catch (OperationCanceledException) when (diagnosisCancellation.IsCancellationRequested)
            {
            }
            catch (Exception aiException)
            {
                logger.LogWarning(aiException, "AI error diagnosis failed for {CorrelationId}", correlationId);
                if (!diagnosisCancellation.IsCancellationRequested)
                {
                    aiText.Text = "AI diagnosis could not be generated. Use the full technical details and Logs folder to continue.";
                    AppendAiResolutionLog(correlationId, module, friendly, aiText.Text);
                }
            }
        };

        window.ShowDialog();
    }

    private static void ActivateExistingWindow()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;

        _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            lock (WindowGate)
            {
                if (_activeWindow is null || !_activeWindow.TryGetTarget(out var existing) || !existing.IsLoaded)
                    return;

                existing.Activate();
                existing.Topmost = true;
                existing.Topmost = false;
            }
        }));
    }

    private static string BuildErrorKey(Exception exception, string module)
        => string.Join('|', module, exception.GetType().FullName, exception.Message);

    private static void TrimOldErrors(DateTimeOffset now)
    {
        foreach (var item in RecentErrors)
        {
            if (now - item.Value > TimeSpan.FromMinutes(2))
                RecentErrors.TryRemove(item.Key, out _);
        }
    }

    private static string GetLogsFolder() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "Logs");

    private static void AppendAiResolutionLog(string correlationId, string module, string friendlyMessage, string aiResolution)
    {
        try
        {
            var folder = GetLogsFolder();
            System.IO.Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, "ai-error-resolutions.log");
            var entry = string.Join(
                Environment.NewLine,
                new string('=', 80),
                $"UTC: {DateTime.UtcNow:O}",
                $"Correlation ID: {correlationId}",
                $"Module: {module}",
                $"User message: {friendlyMessage}",
                aiResolution,
                string.Empty);
            System.IO.File.AppendAllText(path, entry, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            MinWidth = 105
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static string BuildDetails(Exception ex, string module, string id)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Correlation ID: {id}");
        sb.AppendLine($"UTC time: {DateTime.UtcNow:O}");
        sb.AppendLine($"Module: {module}");
        if (ex is AiProviderException ai)
        {
            sb.AppendLine($"Provider: {ai.Provider}");
            sb.AppendLine($"HTTP status: {ai.StatusCode?.ToString() ?? "N/A"}");
        }
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    private static string GetFriendlyMessage(Exception ex) => ex.Message.Contains("credits", StringComparison.OrdinalIgnoreCase)
        ? "The selected AI provider has no remaining credit or quota. Configure a free provider or enable fallback in Settings."
        : ex.Message;

    private static string Redact(string value)
    {
        value = Regex.Replace(value, @"sk-[A-Za-z0-9_-]{12,}", "[REDACTED_API_KEY]", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"AIza[A-Za-z0-9_-]{20,}", "[REDACTED_API_KEY]");
        value = Regex.Replace(value, @"gsk_[A-Za-z0-9_-]{12,}", "[REDACTED_API_KEY]", RegexOptions.IgnoreCase);
        return value;
    }

    private static Window? CurrentMainWindow() => System.Windows.Application.Current?.Windows
        .OfType<Window>()
        .FirstOrDefault(x => x.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}
