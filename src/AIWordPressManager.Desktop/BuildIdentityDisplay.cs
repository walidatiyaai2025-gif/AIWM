using System.Diagnostics;
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
            "Click to copy build information. Ctrl+Click creates a support ZIP. Right-click for support actions.";

        if (!BoundFooters.TryGetValue(footer, out _))
        {
            footer.MouseLeftButtonUp += CopyBuildIdentityToClipboard;
            footer.ContextMenu = CreateSupportContextMenu();
            BoundFooters.Add(footer, new object());
        }
    }

    private static ContextMenu CreateSupportContextMenu()
    {
        var menu = new ContextMenu();

        var copy = new MenuItem { Header = "Copy build information" };
        copy.Click += (_, _) => TryCopyDiagnosticText();
        menu.Items.Add(copy);

        var createBundle = new MenuItem { Header = "Create support bundle ZIP" };
        createBundle.Click += (_, _) => CreateAndRevealSupportBundle();
        menu.Items.Add(createBundle);

        var openFolder = new MenuItem { Header = "Open support bundles folder" };
        openFolder.Click += (_, _) => OpenSupportBundlesFolder();
        menu.Items.Add(openFolder);

        var openSnapshot = new MenuItem { Header = "Open support snapshot" };
        openSnapshot.Click += (_, _) => OpenSupportSnapshot();
        menu.Items.Add(openSnapshot);

        return menu;
    }

    private static void CopyBuildIdentityToClipboard(object sender, MouseButtonEventArgs args)
    {
        try
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                CreateAndRevealSupportBundle();
                if (sender is TextBlock supportFooter)
                    supportFooter.ToolTip = "Support bundle created and selected in File Explorer.";
            }
            else
            {
                TryCopyDiagnosticText();
                if (sender is TextBlock footer)
                {
                    footer.ToolTip =
                        $"Build information copied.\n\n" +
                        $"Application version: {Version}\n" +
                        $"Source branch: {Branch}\n" +
                        $"Source commit: {Commit}\n" +
                        $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}";
                }
            }

            args.Handled = true;
        }
        catch
        {
            // Support actions must remain non-blocking.
        }
    }

    private static void TryCopyDiagnosticText()
    {
        BuildIdentitySupportSnapshot.WriteOnce();
        Clipboard.SetText(DiagnosticText);
    }

    private static void CreateAndRevealSupportBundle()
    {
        BuildIdentitySupportSnapshot.WriteOnce();
        var bundlePath = SupportBundleService.CreateBundle();
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bundlePath}\"") { UseShellExecute = true });
    }

    private static void OpenSupportBundlesFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIWordPressManager",
            "SupportBundles");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private static void OpenSupportSnapshot()
    {
        BuildIdentitySupportSnapshot.WriteOnce();
        if (File.Exists(BuildIdentitySupportSnapshot.SnapshotPath))
            Process.Start(new ProcessStartInfo(BuildIdentitySupportSnapshot.SnapshotPath) { UseShellExecute = true });
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
