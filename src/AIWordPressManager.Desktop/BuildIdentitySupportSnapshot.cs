using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AIWordPressManager.Desktop;

internal static class BuildIdentitySupportSnapshot
{
    private static int _written;

    public static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "Logs",
        "support-snapshot.txt");

    public static void WriteOnce()
    {
        if (Interlocked.Exchange(ref _written, 1) != 0)
            return;

        try
        {
            var directory = Path.GetDirectoryName(SnapshotPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var process = Process.GetCurrentProcess();
            var builder = new StringBuilder()
                .AppendLine("AI WordPress Manager Support Snapshot")
                .AppendLine($"Generated: {DateTimeOffset.Now:O}")
                .AppendLine($"Version: {BuildIdentityDisplay.Version}")
                .AppendLine($"Branch: {BuildIdentityDisplay.Branch}")
                .AppendLine($"Commit: {BuildIdentityDisplay.FullCommit}")
                .AppendLine($"OS: {RuntimeInformation.OSDescription}")
                .AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}")
                .AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}")
                .AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}")
                .AppendLine($"Machine: {Environment.MachineName}")
                .AppendLine($"Windows user: {Environment.UserName}")
                .AppendLine($"Process ID: {Environment.ProcessId}")
                .AppendLine($"Working set MB: {process.WorkingSet64 / 1024d / 1024d:N1}")
                .AppendLine($"Base directory: {AppContext.BaseDirectory}")
                .AppendLine($"Current directory: {Environment.CurrentDirectory}")
                .AppendLine($"Application data: {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");

            File.WriteAllText(SnapshotPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Support diagnostics must never delay or block application startup.
        }
    }
}
