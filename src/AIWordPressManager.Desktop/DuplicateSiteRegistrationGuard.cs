using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels;
using AIWordPressManager.Desktop.ViewModels.Sites;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Prevents users from reaching the save step with a WordPress URL that is
/// already registered. SQLite keeps the unique index as the final safeguard,
/// while this guard converts the situation into a clear journey message.
/// </summary>
internal static class DuplicateSiteRegistrationGuard
{
    private static readonly ConditionalWeakTable<MainWindow, object> AttachedWindows = new();
    private static readonly ConditionalWeakTable<AddSiteWizardViewModel, object> AttachedWizards = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded),
            true);
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || !ReferenceEquals(e.OriginalSource, window))
            return;

        if (AttachedWindows.TryGetValue(window, out _))
            return;

        AttachedWindows.Add(window, new object());

        if (window.DataContext is not MainWindowViewModel main)
            return;

        var wizard = main.Sites.Wizard;
        if (AttachedWizards.TryGetValue(wizard, out _))
            return;

        AttachedWizards.Add(wizard, new object());
        wizard.PropertyChanged += (_, args) => OnWizardPropertyChanged(main.Sites, wizard, args);
    }

    private static void OnWizardPropertyChanged(
        SitesViewModel sites,
        AddSiteWizardViewModel wizard,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AddSiteWizardViewModel.IsConnectionSuccessful) ||
            !wizard.IsConnectionSuccessful)
            return;

        var requestedUrl = NormalizeUrl(wizard.SiteUrl);
        if (string.IsNullOrWhiteSpace(requestedUrl))
            return;

        var existing = sites.Sites.FirstOrDefault(site =>
            string.Equals(NormalizeUrl(site.SiteUrl), requestedUrl, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            return;

        wizard.IsConnectionSuccessful = false;
        wizard.ConnectionMessage = $"{existing.Name} is already registered in this application.";
        wizard.ValidationMessage =
            "This WordPress URL already exists. Close this wizard and select the existing website card. " +
            "Use the card right-click menu to retest, synchronize, open WordPress admin, or remove it before registering again.";
        wizard.DiagnosticsTitle = "Website already registered";
        wizard.DiagnosticsText =
            $"Existing website: {existing.Name}{Environment.NewLine}" +
            $"URL: {existing.SiteUrl}{Environment.NewLine}{Environment.NewLine}" +
            "Duplicate website records are blocked to protect synchronization history, jobs, approvals, and audit evidence.";
        wizard.IsDiagnosticsOpen = true;
    }

    private static string NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return text.TrimEnd('/').ToLowerInvariant();

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty,
            Query = string.Empty
        };

        if ((builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443) ||
            (builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80))
            builder.Port = -1;

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
    }
}
