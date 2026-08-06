using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class HelpViewModel
{
    [RelayCommand]
    private void CopySupportSummary()
    {
        var latestBundle = FindLatestSupportBundlePath();
        if (!string.IsNullOrWhiteSpace(latestBundle) && File.Exists(latestBundle))
        {
            LatestSupportBundlePath = latestBundle;
            ApplyBundleVerification(latestBundle);
        }

        var summary = string.Join(
            Environment.NewLine,
            "AI WordPress Manager Support Summary",
            $"Version: {BuildIdentityDisplay.Version}",
            $"Branch: {BuildIdentityDisplay.Branch}",
            $"Commit: {BuildIdentityDisplay.FullCommit}",
            $"Integrity: {SupportBundleVerificationStatus}",
            $"Build compatibility: {SupportBundleBuildCompatibilityStatus}",
            $"Latest support bundle: {LatestSupportBundlePath}",
            $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}");

        BuildIdentitySupportSnapshot.WriteOnce();
        Clipboard.SetText(summary);
        GuideStatus = "Support summary copied to the clipboard.";
    }
}
