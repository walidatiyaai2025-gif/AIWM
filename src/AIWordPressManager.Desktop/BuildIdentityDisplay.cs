using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop;

internal static class BuildIdentityDisplay
{
    private const string FooterMarker = "AI WordPress Website Manager • Offline-first";
    private static readonly ConditionalWeakTable<TextBlock, object> BoundFooters = new();

    public static string Version { get; } = ResolveVersion();
    public static string Branch { get; } = ResolveMetadata("SourceBranch", "unknown");
    public static string FullCommit { get; } = ResolveMetadata("SourceCommit", "unknown");
    public static string Commit { get; } = FullCommit.Length > 8 ? FullCommit[..8] : FullCommit;
    public static string DisplayText => $"Version {Version} • Branch {Branch}";
    public static string DiagnosticText =>
        $"AI WordPress Manager{Environment.NewLine}" +
        $"Version: {Version}{Environment.NewLine}" +
        $"Branch: {Branch}{Environment.NewLine}" +
        $"Commit: {FullCommit}{Environment.NewLine}" +
        $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}";

    public static void Apply(Window window)
    {
        BuildIdentitySupportSnapshot.WriteOnce();
        window.Title = $"AI WordPress Management • {DisplayText}";

        var footer = FindFooterTextBlock(window);
        if (footer is null)
            return;

        footer.Text = DisplayText;
        footer.FontWeight = FontWeights.SemiBold;
        footer.Cursor = Cursors.Hand;
        footer.ToolTip =
            $"Application version: {Version}\n" +
            $"Source branch: {Branch}\n" +
            $"Source commit: {Commit}\n" +
            $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}\n\n" +
            "Click to copy complete build information.";

        if (!BoundFooters.TryGetValue(footer, out _))
        {
            footer.MouseLeftButtonUp += CopyBuildIdentityToClipboard;
            BoundFooters.Add(footer, new object());
        }
    }

    private static void CopyBuildIdentityToClipboard(object sender, MouseButtonEventArgs args)
    {
        try
        {
            BuildIdentitySupportSnapshot.WriteOnce();
            Clipboard.SetText(DiagnosticText);
            if (sender is TextBlock footer)
            {
                footer.ToolTip =
                    $"Build information copied.\n\n" +
                    $"Application version: {Version}\n" +
                    $"Source branch: {Branch}\n" +
                    $"Source commit: {Commit}\n" +
                    $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}";
            }

            args.Handled = true;
        }
        catch
        {
            // Clipboard access can fail when another process temporarily owns it.
            // Build identity display must remain non-blocking.
        }
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

    private static string ResolveMetadata(string key, string fallback)
    {
        var metadata = typeof(BuildIdentityDisplay).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(metadata?.Value) ? fallback : metadata.Value;
    }
}
