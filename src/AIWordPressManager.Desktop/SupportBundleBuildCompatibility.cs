using System.IO.Compression;
using System.Text;

namespace AIWordPressManager.Desktop;

internal static class SupportBundleBuildCompatibility
{
    public static SupportBundleBuildCompatibilityResult Inspect(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            return SupportBundleBuildCompatibilityResult.Unavailable("Support bundle file was not found.");

        try
        {
            using var archive = ZipFile.OpenRead(bundlePath);
            var manifestEntry = archive.GetEntry("manifest.txt");
            if (manifestEntry is null)
                return SupportBundleBuildCompatibilityResult.Unavailable("manifest.txt is missing.");

            string manifest;
            using (var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                manifest = reader.ReadToEnd();

            var bundleVersion = ReadManifestValue(manifest, "Version:");
            var bundleBranch = ReadManifestValue(manifest, "Branch:");
            var bundleCommit = ReadManifestValue(manifest, "Commit:");

            if (string.IsNullOrWhiteSpace(bundleVersion) ||
                string.IsNullOrWhiteSpace(bundleBranch) ||
                string.IsNullOrWhiteSpace(bundleCommit))
            {
                return SupportBundleBuildCompatibilityResult.Unavailable(
                    "The support bundle manifest does not contain complete build identity information.");
            }

            var versionMatches = string.Equals(
                bundleVersion,
                BuildIdentityDisplay.Version,
                StringComparison.OrdinalIgnoreCase);
            var branchMatches = string.Equals(
                bundleBranch,
                BuildIdentityDisplay.Branch,
                StringComparison.OrdinalIgnoreCase);
            var commitMatches = string.Equals(
                bundleCommit,
                BuildIdentityDisplay.FullCommit,
                StringComparison.OrdinalIgnoreCase);

            return new SupportBundleBuildCompatibilityResult(
                IsAvailable: true,
                IsCurrentBuild: versionMatches && branchMatches && commitMatches,
                BundleVersion: bundleVersion,
                BundleBranch: bundleBranch,
                BundleCommit: bundleCommit,
                VersionMatches: versionMatches,
                BranchMatches: branchMatches,
                CommitMatches: commitMatches,
                Message: versionMatches && branchMatches && commitMatches
                    ? "The support bundle belongs to the currently running build."
                    : "The support bundle is valid but belongs to a different application build.");
        }
        catch (Exception exception)
        {
            return SupportBundleBuildCompatibilityResult.Unavailable(
                $"Build compatibility inspection failed: {exception.Message}");
        }
    }

    private static string? ReadManifestValue(string manifest, string prefix)
    {
        foreach (var rawLine in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].Trim();
        }

        return null;
    }
}

internal sealed record SupportBundleBuildCompatibilityResult(
    bool IsAvailable,
    bool IsCurrentBuild,
    string? BundleVersion,
    string? BundleBranch,
    string? BundleCommit,
    bool VersionMatches,
    bool BranchMatches,
    bool CommitMatches,
    string Message)
{
    public static SupportBundleBuildCompatibilityResult Unavailable(string message) =>
        new(
            IsAvailable: false,
            IsCurrentBuild: false,
            BundleVersion: null,
            BundleBranch: null,
            BundleCommit: null,
            VersionMatches: false,
            BranchMatches: false,
            CommitMatches: false,
            Message: message);
}
