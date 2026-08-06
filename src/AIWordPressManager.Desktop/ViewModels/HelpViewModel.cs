using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using AIWordPressManager.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class HelpViewModel : ObservableObject
{
    private const string GuideFileName = "AIWordPressManager_UserGuide_AR.docx";
    private const string StatusRoadmapFileName = "AIWordPressManager_System_Status_Roadmap_AR.docx";
    private readonly IDialogService _dialogService;

    public ObservableCollection<ShortcutItem> Shortcuts { get; } =
    [
        new("F1", "Context help", "Enable contextual help for the control under the pointer."),
        new("Ctrl + F1", "Guided workflow tour", "Resume or restart the interactive tour from site setup through verified execution."),
        new("Shift + F1", "Open user guide", "Open the bundled Word document."),
        new("Ctrl + Click version", "Create support bundle", "Create a ZIP with build identity, logs, diagnostics, and recent execution receipts."),
        new("Ctrl + 1", "Dashboard", "Open the live dashboard."),
        new("Ctrl + 2", "Sites", "Open website management."),
        new("Ctrl + 3", "WordPress Explorer", "Open synchronized WordPress data."),
        new("Ctrl + 4", "SEO Audit", "Open SEO findings."),
        new("Ctrl + 5", "Suggested Changes", "Review generated proposals."),
        new("Ctrl + 6", "Execution Center", "Open safe execution workflow."),
        new("Ctrl + 7", "Jobs", "Open background jobs."),
        new("Ctrl + 8", "Settings", "Open application settings."),
        new("Ctrl + H", "Help & shortcuts", "Open this screen."),
        new("Ctrl + Shift + R", "Refresh current screen", "Reload the current screen from local storage."),
        new("Ctrl + Shift + L", "Switch language", "Toggle Arabic and English."),
        new("Ctrl + Shift + T", "Switch theme", "Toggle light and dark mode."),
        new("Ctrl + Shift + P", "Command Palette", "Open screens and run memory cleanup commands.")
    ];

    [ObservableProperty] private string _guideVersion = "Part 81";
    [ObservableProperty] private string _guideStatus = "The bundled guide and interactive tour are ready.";
    [ObservableProperty] private string _latestSupportBundlePath = FindLatestSupportBundlePath() ?? "No support bundle has been created yet.";

    public IAsyncRelayCommand OpenGuideCommand { get; }
    public IAsyncRelayCommand OpenStatusRoadmapCommand { get; }
    public IAsyncRelayCommand OpenDocumentationFolderCommand { get; }
    public IAsyncRelayCommand CreateSupportBundleCommand { get; }
    public IAsyncRelayCommand OpenSupportFolderCommand { get; }
    public IAsyncRelayCommand OpenLatestSupportBundleCommand { get; }
    public IRelayCommand ResumeGuidedTourCommand { get; }
    public IRelayCommand RestartGuidedTourCommand { get; }

    public HelpViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        OpenGuideCommand = new AsyncRelayCommand(OpenGuideAsync);
        OpenStatusRoadmapCommand = new AsyncRelayCommand(OpenStatusRoadmapAsync);
        OpenDocumentationFolderCommand = new AsyncRelayCommand(OpenDocumentationFolderAsync);
        CreateSupportBundleCommand = new AsyncRelayCommand(CreateSupportBundleAsync);
        OpenSupportFolderCommand = new AsyncRelayCommand(OpenSupportFolderAsync);
        OpenLatestSupportBundleCommand = new AsyncRelayCommand(OpenLatestSupportBundleAsync);
        ResumeGuidedTourCommand = new RelayCommand(() => ShowGuidedTour(restart: false));
        RestartGuidedTourCommand = new RelayCommand(() => ShowGuidedTour(restart: true));
    }

    public string GuidePath => Path.Combine(AppContext.BaseDirectory, "Documentation", GuideFileName);
    public string StatusRoadmapPath => Path.Combine(AppContext.BaseDirectory, "Documentation", StatusRoadmapFileName);

    private static string SupportFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "SupportBundles");

    private void ShowGuidedTour(bool restart)
    {
        if (System.Windows.Application.Current is not App)
        {
            GuideStatus = "The guided tour is available after the desktop workspace has opened.";
            return;
        }

        App.ShowGuidedTour(restart);
        GuideStatus = restart
            ? "The guided tour was restarted from the first step."
            : "The guided tour was opened at the saved step.";
    }

    private async Task CreateSupportBundleAsync()
    {
        try
        {
            BuildIdentitySupportSnapshot.WriteOnce();
            var bundlePath = SupportBundleService.CreateBundle();
            LatestSupportBundlePath = bundlePath;
            GuideStatus = $"Support bundle created: {Path.GetFileName(bundlePath)}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bundlePath}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot create support bundle", exception.Message);
        }
    }

    private async Task OpenSupportFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(SupportFolderPath);
            Process.Start(new ProcessStartInfo("explorer.exe", SupportFolderPath) { UseShellExecute = true });
            GuideStatus = "Opened the support bundles folder.";
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot open support folder", exception.Message);
        }
    }

    private async Task OpenLatestSupportBundleAsync()
    {
        var latest = FindLatestSupportBundlePath();
        if (string.IsNullOrWhiteSpace(latest) || !File.Exists(latest))
        {
            GuideStatus = "No support bundle has been created yet.";
            await _dialogService.ShowErrorAsync("Support bundle not found", GuideStatus);
            return;
        }

        try
        {
            LatestSupportBundlePath = latest;
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{latest}\"") { UseShellExecute = true });
            GuideStatus = $"Selected {Path.GetFileName(latest)}.";
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot open support bundle", exception.Message);
        }
    }

    private static string? FindLatestSupportBundlePath()
    {
        if (!Directory.Exists(SupportFolderPath))
            return null;

        return Directory.EnumerateFiles(SupportFolderPath, "AIWordPressManager_Support_*.zip")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task OpenGuideAsync()
    {
        if (!File.Exists(GuidePath))
        {
            GuideStatus = $"User guide was not found at: {GuidePath}";
            await _dialogService.ShowErrorAsync("User guide not found", GuideStatus);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(GuidePath) { UseShellExecute = true });
            GuideStatus = $"Opened {GuideFileName}.";
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot open user guide", exception.Message);
        }
    }

    private async Task OpenStatusRoadmapAsync()
    {
        if (!File.Exists(StatusRoadmapPath))
        {
            GuideStatus = $"System status document was not found at: {StatusRoadmapPath}";
            await _dialogService.ShowErrorAsync("System status document not found", GuideStatus);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(StatusRoadmapPath) { UseShellExecute = true });
            GuideStatus = $"Opened {StatusRoadmapFileName}.";
        }
        catch (Exception exception)
        {
            GuideStatus = exception.Message;
            await _dialogService.ShowErrorAsync("Cannot open system status document", exception.Message);
        }
    }

    private async Task OpenDocumentationFolderAsync()
    {
        var folder = Path.GetDirectoryName(GuidePath)!;
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            await _dialogService.ShowErrorAsync("Cannot open documentation folder", exception.Message);
        }
    }
}

public sealed record ShortcutItem(string Keys, string Action, string Description);
