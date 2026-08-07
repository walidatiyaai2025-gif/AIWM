using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class HelpViewModel
{
    [RelayCommand]
    private async Task CopySupportSummaryAsync()
    {
        try
        {
            var latestBundle = FindLatestSupportBundlePath();
            if (!string.IsNullOrWhiteSpace(latestBundle) && File.Exists(latestBundle))
            {
                LatestSupportBundlePath = latestBundle;
                ApplyBundleVerification(latestBundle);
            }

            BuildIdentitySupportSnapshot.WriteOnce();
            var summary = string.Join(
                Environment.NewLine,
                "AI WordPress Manager Support Summary",
                $"Generated UTC: {DateTimeOffset.UtcNow:O}",
                $"Computer: {Environment.MachineName}",
                $"Version: {BuildIdentityDisplay.Version}",
                $"Branch: {BuildIdentityDisplay.Branch}",
                $"Commit: {BuildIdentityDisplay.FullCommit}",
                $"Integrity: {SupportBundleVerificationStatus}",
                $"Build compatibility: {SupportBundleBuildCompatibilityStatus}",
                $"Latest support bundle: {LatestSupportBundlePath}",
                $"Support snapshot: {BuildIdentitySupportSnapshot.SnapshotPath}");

            Clipboard.SetText(summary);
            GuideStatus = "Support summary copied to the clipboard.";
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot copy support summary", exception.Message);
        }
    }
}
