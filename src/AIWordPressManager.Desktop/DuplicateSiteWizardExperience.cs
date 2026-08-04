using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

internal static class DuplicateSiteWizardExperience
{
    private sealed class ButtonState
    {
        public object? OriginalContent { get; init; }
        public ICommand? OriginalCommand { get; init; }
        public object? OriginalCommandParameter { get; init; }
        public bool DuplicateMode { get; set; }
    }

    private static readonly ConditionalWeakTable<Button, ButtonState> States = new();
    private static readonly ConditionalWeakTable<AddSiteWizardViewModel, object> SubscribedWizards = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnButtonLoaded),
            true);
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not AddSiteWizardViewModel wizard)
            return;

        var text = button.Content?.ToString() ?? string.Empty;
        if (!text.Contains("Save", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("sync", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("existing site", StringComparison.OrdinalIgnoreCase))
            return;

        if (!States.TryGetValue(button, out _))
        {
            States.Add(button, new ButtonState
            {
                OriginalContent = button.Content,
                OriginalCommand = button.Command,
                OriginalCommandParameter = button.CommandParameter
            });
            button.Click += OnActionButtonClick;
        }

        if (!SubscribedWizards.TryGetValue(wizard, out _))
        {
            SubscribedWizards.Add(wizard, new object());
            wizard.PropertyChanged += OnWizardPropertyChanged;
        }

        UpdateButton(button, wizard);
    }

    private static void OnWizardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AddSiteWizardViewModel wizard ||
            e.PropertyName is not (nameof(AddSiteWizardViewModel.ValidationMessage) or nameof(AddSiteWizardViewModel.IsOpen)))
            return;

        var application = global::System.Windows.Application.Current;
        if (application?.Dispatcher is null)
            return;

        application.Dispatcher.BeginInvoke(() =>
        {
            foreach (Window window in application.Windows)
                UpdateButtons(window, wizard);
        });
    }

    private static void UpdateButtons(DependencyObject root, AddSiteWizardViewModel wizard)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is Button button && ReferenceEquals(button.DataContext, wizard) && States.TryGetValue(button, out _))
                UpdateButton(button, wizard);
            UpdateButtons(child, wizard);
        }
    }

    private static void UpdateButton(Button button, AddSiteWizardViewModel wizard)
    {
        if (!States.TryGetValue(button, out var state))
            return;

        var duplicate = IsDuplicateState(wizard.ValidationMessage);
        state.DuplicateMode = duplicate;

        if (duplicate)
        {
            button.Content = "Open existing site";
            button.Command = null;
            button.CommandParameter = null;
            button.IsEnabled = true;
            button.ToolTip = "Close this wizard, select the registered website, and continue its journey.";
        }
        else
        {
            button.Content = state.OriginalContent;
            button.Command = state.OriginalCommand;
            button.CommandParameter = state.OriginalCommandParameter;
            button.ToolTip = "Save the website and automatically start its first synchronization.";
        }
    }

    private static async void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            !States.TryGetValue(button, out var state) ||
            !state.DuplicateMode ||
            button.DataContext is not AddSiteWizardViewModel wizard)
            return;

        e.Handled = true;

        var mainWindow = global::System.Windows.Application.Current?.Windows
            .OfType<MainWindow>()
            .FirstOrDefault();
        if (mainWindow?.DataContext is not MainWindowViewModel main)
            return;

        wizard.IsOpen = false;
        await main.Sites.LoadAsync();

        var normalized = NormalizeUrl(wizard.SiteUrl);
        var existing = main.Sites.Sites.FirstOrDefault(site =>
            string.Equals(NormalizeUrl(site.SiteUrl), normalized, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            await main.Sites.SelectSiteCommand.ExecuteAsync(existing);

        main.NavigateCommand.Execute("Sites");
        main.RefreshCompleteUserJourney();
        main.Operations.Start(
            "Existing website opened",
            "Continuing the saved website journey",
            "The duplicate wizard was closed and the existing website was selected.",
            100);
        main.Operations.Complete(
            "Use the website card or its right-click menu to synchronize, retest the connection, open WordPress admin, or continue the guided journey.");
    }

    private static bool IsDuplicateState(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("already registered", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return (value ?? string.Empty).Trim().TrimEnd('/');

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Host = uri.Host.ToLowerInvariant()
        };

        if ((builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && builder.Port == 443) ||
            (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && builder.Port == 80))
            builder.Port = -1;

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
