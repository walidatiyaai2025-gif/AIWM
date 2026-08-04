using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace AIWordPressManager.Setup;

internal static class Program
{
    private const string Prefix = "AIWP.Payload.";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AIWordPressManager");
            Directory.CreateDirectory(installRoot);

            var assembly = Assembly.GetExecutingAssembly();
            var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
            if (resources.Length == 0)
                throw new InvalidOperationException("The installer payload is empty. Build the Setup project in Release configuration.");

            foreach (var resource in resources)
            {
                var relative = DecodeResourcePath(resource[Prefix.Length..]);
                var target = Path.Combine(installRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Missing resource: {resource}");
                using var output = File.Create(target);
                input.CopyTo(output);
            }

            var exe = Path.Combine(installRoot, "AIWordPressManager.Desktop.exe");
            CreateShortcut(exe, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AI WordPress Manager.lnk"));
            CreateShortcut(exe, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "AI WordPress Manager.lnk"));

            MessageBox.Show("AI WordPress Manager was installed successfully.", "AI WordPress Manager Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "AI WordPress Manager Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string DecodeResourcePath(string encoded)
    {
        // RecursiveDir is encoded with dots in manifest names. Known file extensions
        // are retained; directory dots are converted to separators.
        var known = new[] { ".dll", ".exe", ".json", ".config", ".pdb", ".docx", ".md", ".dat", ".xml" };
        var extension = known.FirstOrDefault(e => encoded.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ?? Path.GetExtension(encoded);
        var body = extension.Length > 0 ? encoded[..^extension.Length] : encoded;
        return body.Replace('.', Path.DirectorySeparatorChar) + extension;
    }

    private static void CreateShortcut(string target, string shortcutPath)
    {
        if (!File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.Description = "AI WordPress Website Manager";
        shortcut.Save();
    }
}
