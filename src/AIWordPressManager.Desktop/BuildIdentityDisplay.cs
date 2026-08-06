using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

internal static class BuildIdentityDisplay
{
    private const string FooterMarker = "AI WordPress Website Manager • Offline-first";

    public static string Version { get; } = ResolveVersion();
    public static string Branch { get; } = ResolveMetadata("SourceBranch", "unknown");
    public static string Commit { get; } = ResolveCommit();
    public static string DisplayText => $"Version {Version} • Branch {Branch}";

    public static void Apply(Window window)
    {
        window.Title = $"AI WordPress Management • {DisplayText}";

        var footer = FindFooterTextBlock(window);
        if (footer is null)
            return;

        footer.Text = DisplayText;
        footer.FontWeight = FontWeights.SemiBold;
        footer.ToolTip = $"Application version: {Version}\nSource branch: {Branch}\nSource commit: {Commit}";
    }

    private static TextBlock? FindFooterTextBlock(DependencyObject parent)
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock && string.Equals(textBlock.Text, FooterMarker, StringComparison.Ordinal))
                return textBlock;

            var nested = FindFooterTextBlock(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(BuildIdentityDisplay).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string ResolveCommit()
    {
        var value = ResolveMetadata("SourceCommit", "unknown");
        return value.Length > 8 ? value[..8] : value;
    }

    private static string ResolveMetadata(string key, string fallback)
    {
        var metadata = typeof(BuildIdentityDisplay).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(metadata?.Value) ? fallback : metadata.Value;
    }
}
