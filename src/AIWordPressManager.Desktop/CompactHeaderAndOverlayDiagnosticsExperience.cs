using System.Runtime.CompilerServices;

namespace AIWordPressManager.Desktop;

/// <summary>
/// Disabled during UI recovery. Header compaction and overlay diagnostics must not modify
/// the visual tree from a module initializer.
/// </summary>
internal static class CompactHeaderAndOverlayDiagnosticsExperience
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Intentionally disabled.
    }
}
