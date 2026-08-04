using System.Runtime.CompilerServices;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Disabled safety shim. The previous implementation registered a Loaded handler for every
/// FrameworkElement and could collapse the main visual tree, causing a black application window.
/// UI cleanup must be implemented explicitly in XAML or page-specific code instead.
/// </summary>
internal static class StableUiAndHeaderExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Intentionally disabled.
    }
}
