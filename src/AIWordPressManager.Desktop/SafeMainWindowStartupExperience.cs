using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIWordPressManager.Desktop.Services;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Recovery guard used only during startup. It prevents the expensive runtime-localization
/// visual-tree scan from running before the first frame is rendered and guarantees that the
/// shell root remains visible. The normal language toggle still works after startup.
/// </summary>
internal static class SafeMainWindowStartupExperience
{
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

        ForceSafeStartupLanguage(window);

        window.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(() =>
            {
                window.Opacity = 1;
                window.Visibility = Visibility.Visible;
                window.IsHitTestVisible = true;

                if (window.Content is FrameworkElement root)
                {
                    root.Visibility = Visibility.Visible;
                    root.Opacity = 1;
                    root.IsHitTestVisible = true;
                    Panel.SetZIndex(root, 0);
                }
            }));
    }

    private static void ForceSafeStartupLanguage(MainWindow window)
    {
        try
        {
            var field = typeof(MainWindow).GetField("_localization", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(window) is not ILocalizationService localization)
                return;

            var property = localization.GetType().GetProperty(
                nameof(ILocalizationService.IsArabic),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            property?.SetValue(localization, false);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            window.FlowDirection = FlowDirection.LeftToRight;
            window.Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");
        }
        catch
        {
            // Startup recovery must never interrupt window creation.
        }
    }
}
