using System.Collections.ObjectModel;
using System.Diagnostics;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Application.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class AiProviderSettingItem : ObservableObject
{
    public required string Provider { get; init; }
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _priority;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private bool _hasStoredApiKey;
    [ObservableProperty] private string _status = "Not tested";
    public string CapabilityHint => Provider switch
    {
        "Puter" => "Browser-created personal token. Temperature is omitted because several Puter-routed models only accept their default value.",
        "Ollama" => "Local provider. No API key is required; make sure the Ollama service is running.",
        "Gemini" => "Google Gemini API key provider with model-specific generation settings.",
        "Groq" => "Fast hosted inference. Model and quota availability depend on your Groq account.",
        "OpenRouter" => "Unified router. Free models and limits can change over time.",
        "OpenAI" => "OpenAI API billing is separate from a ChatGPT subscription.",
        _ => "Provider-specific capabilities are applied automatically."
    };
    public bool RequiresApiKey => !Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IApplicationSettingsService _service;
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;
    private readonly ISecretProtectionService _secretProtection;

    public ObservableCollection<AiProviderSettingItem> AiProviders { get; } = [];
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand<AiProviderSettingItem> TestProviderCommand { get; }
    public IRelayCommand OpenPuterAuthenticationCommand { get; }
    public IAsyncRelayCommand ValidateAiAutomationCommand { get; }

    [ObservableProperty] private int _intervalMinutes = 60;
    [ObservableProperty] private bool _runOnStartup = true;
    [ObservableProperty] private bool _offlineFirst = true;
    [ObservableProperty] private bool _enableAiRecommendations = true;
    [ObservableProperty] private bool _automaticAiFallback = true;
    [ObservableProperty] private bool _enableContentTrash = true;
    [ObservableProperty] private bool _enablePermanentContentDelete;
    [ObservableProperty] private bool _enablePermanentMediaDelete;
    [ObservableProperty] private bool _requireBackupBeforePermanentDelete = true;
    [ObservableProperty] private bool _enableMemoryCooling = true;
    [ObservableProperty] private int _memoryCoolingThresholdPercent = 80;
    [ObservableProperty] private int _memoryCoolingResumePercent = 72;
    [ObservableProperty] private int _memoryCoolingCheckIntervalSeconds = 5;
    [ObservableProperty] private bool _killChildProcessesOnExit = true;
    [ObservableProperty] private bool _pauseJobsAfterFailures = true;
    [ObservableProperty] private int _consecutiveJobFailuresBeforePause = 3;
    [ObservableProperty] private int _jobFailurePauseMinutes = 15;
    [ObservableProperty] private bool _autoResumeJobsAfterPause = true;
    [ObservableProperty] private bool _enableAiErrorDiagnosis = true;
    [ObservableProperty] private string _errorDecisionMode = "Ask";
    [ObservableProperty] private bool _autoExecuteLowRiskAiActions;
    [ObservableProperty] private bool _autoRejectHighRiskAiActions = true;
    [ObservableProperty] private bool _captureBeforeAfterEvidence = true;
    [ObservableProperty] private bool _requireVerifiedExecutionResult = true;
    [ObservableProperty] private int _minimumSplashSeconds = 3;
    [ObservableProperty] private string _statusMessage = "Synchronization, AI providers, and safety settings are stored locally in SQLite.";
    [ObservableProperty] private string _aiAutomationReadiness = "Not validated yet.";
    [ObservableProperty] private string _aiAutomationSafetySummary = "Validation checks provider availability, execution safeguards, evidence capture, and verification requirements.";
    [ObservableProperty] private bool _isBusy;

    public SettingsViewModel(IApplicationSettingsService service, IEnumerable<IAiProvider> providers, ISecretProtectionService secretProtection)
    {
        _service = service;
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _secretProtection = secretProtection;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        TestProviderCommand = new AsyncRelayCommand<AiProviderSettingItem>(TestProviderAsync, item => item is not null && !IsBusy);
        OpenPuterAuthenticationCommand = new RelayCommand(OpenPuterAuthentication);
        ValidateAiAutomationCommand = new AsyncRelayCommand(ValidateAiAutomationAsync, () => !IsBusy);
    }

    partial void OnIsBusyChanged(bool value) { SaveCommand.NotifyCanExecuteChanged(); TestProviderCommand.NotifyCanExecuteChanged(); ValidateAiAutomationCommand.NotifyCanExecuteChanged(); }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var synchronization = await _service.GetSynchronizationSettingsAsync();
            IntervalMinutes = synchronization.IntervalMinutes; RunOnStartup = synchronization.RunOnStartup; OfflineFirst = synchronization.OfflineFirst;
            var ai = await _service.GetAiSettingsAsync();
            EnableAiRecommendations = ai.Enabled; AutomaticAiFallback = ai.AutomaticFallback;
            AiProviders.Clear();
            foreach (var provider in ai.Providers.OrderBy(x => x.Priority))
                AiProviders.Add(new AiProviderSettingItem { Provider = provider.Provider, Enabled = provider.Enabled, Priority = provider.Priority, Model = provider.Model, HasStoredApiKey = provider.HasApiKey });
            var performance = await _service.GetPerformanceSettingsAsync();
            EnableMemoryCooling = performance.EnableMemoryCooling; MemoryCoolingThresholdPercent = performance.CoolingThresholdPercent; MemoryCoolingResumePercent = performance.ResumeThresholdPercent; MemoryCoolingCheckIntervalSeconds = performance.CheckIntervalSeconds; KillChildProcessesOnExit = performance.KillChildProcessesOnExit;
            var jobs = await _service.GetJobReliabilitySettingsAsync();
            PauseJobsAfterFailures = jobs.PauseAfterFailures; ConsecutiveJobFailuresBeforePause = jobs.ConsecutiveFailuresBeforePause; JobFailurePauseMinutes = jobs.FailurePauseMinutes; AutoResumeJobsAfterPause = jobs.AutoResumeAfterPause;
            var automation = await _service.GetAiAutomationSettingsAsync();
            EnableAiErrorDiagnosis = automation.EnableAiErrorDiagnosis; ErrorDecisionMode = automation.ErrorDecisionMode;
            AutoExecuteLowRiskAiActions = automation.AutoExecuteLowRiskAiActions; AutoRejectHighRiskAiActions = automation.AutoRejectHighRiskAiActions;
            CaptureBeforeAfterEvidence = automation.CaptureBeforeAfterEvidence; RequireVerifiedExecutionResult = automation.RequireVerifiedExecutionResult; MinimumSplashSeconds = automation.MinimumSplashSeconds;
            var destructive = await _service.GetDestructiveOperationSettingsAsync();
            EnableContentTrash = destructive.EnableContentTrash; EnablePermanentContentDelete = destructive.EnablePermanentContentDelete;
            EnablePermanentMediaDelete = destructive.EnablePermanentMediaDelete; RequireBackupBeforePermanentDelete = destructive.RequireBackupBeforePermanentDelete;
            UpdateAiAutomationReadiness();
            StatusMessage = "Settings loaded from the local database.";
        }
        finally { IsBusy = false; }
    }

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            IntervalMinutes = Math.Clamp(IntervalMinutes, 5, 1440);
            await _service.SaveSynchronizationSettingsAsync(new(IntervalMinutes, RunOnStartup, OfflineFirst));
            var providerSettings = AiProviders.Select(x => new AiProviderSettings(x.Provider, x.Enabled, Math.Clamp(x.Priority, 1, 20), x.Model.Trim(), string.Empty, x.HasStoredApiKey)).ToArray();
            var keys = AiProviders.ToDictionary(x => x.Provider, x => string.IsNullOrWhiteSpace(x.ApiKey) ? null : x.ApiKey, StringComparer.OrdinalIgnoreCase);
            await _service.SaveAiSettingsAsync(new AiSettings(EnableAiRecommendations, AutomaticAiFallback, providerSettings), keys);
            foreach (var item in AiProviders) { item.HasStoredApiKey |= !string.IsNullOrWhiteSpace(item.ApiKey); item.ApiKey = string.Empty; }
            MemoryCoolingThresholdPercent = Math.Clamp(MemoryCoolingThresholdPercent, 50, 98);
            MemoryCoolingResumePercent = Math.Clamp(MemoryCoolingResumePercent, 40, MemoryCoolingThresholdPercent - 1);
            MemoryCoolingCheckIntervalSeconds = Math.Clamp(MemoryCoolingCheckIntervalSeconds, 1, 60);
            await _service.SavePerformanceSettingsAsync(new(EnableMemoryCooling, MemoryCoolingThresholdPercent, MemoryCoolingResumePercent, MemoryCoolingCheckIntervalSeconds, KillChildProcessesOnExit));
            ConsecutiveJobFailuresBeforePause = Math.Clamp(ConsecutiveJobFailuresBeforePause, 1, 20);
            JobFailurePauseMinutes = Math.Clamp(JobFailurePauseMinutes, 1, 1440);
            await _service.SaveJobReliabilitySettingsAsync(new(PauseJobsAfterFailures, ConsecutiveJobFailuresBeforePause, JobFailurePauseMinutes, AutoResumeJobsAfterPause));
            MinimumSplashSeconds = Math.Clamp(MinimumSplashSeconds, 3, 30);
            if (AutoExecuteLowRiskAiActions)
            {
                ErrorDecisionMode = "AutoLowRisk";
                AutoRejectHighRiskAiActions = true;
                CaptureBeforeAfterEvidence = true;
                RequireVerifiedExecutionResult = true;
            }
            await _service.SaveAiAutomationSettingsAsync(new(EnableAiErrorDiagnosis, ErrorDecisionMode, AutoExecuteLowRiskAiActions, AutoRejectHighRiskAiActions, CaptureBeforeAfterEvidence, RequireVerifiedExecutionResult, MinimumSplashSeconds));
            await _service.SaveDestructiveOperationSettingsAsync(new(EnableContentTrash, EnablePermanentContentDelete, EnablePermanentMediaDelete, RequireBackupBeforePermanentDelete));
            UpdateAiAutomationReadiness();
            StatusMessage = "Settings saved. AI diagnosis, approval policy, evidence capture, and execution verification are active according to this profile.";
        }
        finally { IsBusy = false; }
    }

    private void OpenPuterAuthentication()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://puter.com/dashboard",
            UseShellExecute = true
        });
        StatusMessage = "Chrome opened Puter. Sign in, create an API token from the dashboard, then paste it into the Puter provider field and save.";
    }

    private async Task TestProviderAsync(AiProviderSettingItem? item)
    {
        if (item is null || !_providers.TryGetValue(item.Provider, out var provider)) return;
        IsBusy = true;
        try
        {
            string key;
            if (!string.IsNullOrWhiteSpace(item.ApiKey)) key = item.ApiKey.Trim();
            else
            {
                var settings = await _service.GetAiSettingsAsync();
                var saved = settings.Providers.FirstOrDefault(x => x.Provider.Equals(item.Provider, StringComparison.OrdinalIgnoreCase));
                if (item.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)) key = string.Empty;
                else
                {
                    if (saved is null || string.IsNullOrWhiteSpace(saved.ProtectedApiKey)) { item.Status = "Enter an API key first."; return; }
                    key = await _secretProtection.UnprotectAsync(saved.ProtectedApiKey);
                }
            }
            var result = await provider.TestAsync(item.Model, key);
            item.Status = result.Message;
            if (result.Success && result.Models.Count > 0 && !result.Models.Contains(item.Model)) item.Status += $" Available models: {string.Join(", ", result.Models.Take(6))}";
        }
        catch (Exception ex) { item.Status = ex.Message; }
        finally { IsBusy = false; }
    }
    private async Task ValidateAiAutomationAsync()
    {
        IsBusy = true;
        try
        {
            var enabledProviders = AiProviders.Where(x => x.Enabled).OrderBy(x => x.Priority).ToArray();
            var readyProviders = 0;
            foreach (var item in enabledProviders)
            {
                if (!_providers.TryGetValue(item.Provider, out var provider)) continue;
                try
                {
                    string key = string.Empty;
                    if (!item.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(item.ApiKey)) key = item.ApiKey.Trim();
                        else
                        {
                            var settings = await _service.GetAiSettingsAsync();
                            var saved = settings.Providers.FirstOrDefault(x => x.Provider.Equals(item.Provider, StringComparison.OrdinalIgnoreCase));
                            if (saved is null || string.IsNullOrWhiteSpace(saved.ProtectedApiKey))
                            {
                                item.Status = "Credential required.";
                                continue;
                            }
                            key = await _secretProtection.UnprotectAsync(saved.ProtectedApiKey);
                        }
                    }

                    var result = await provider.TestAsync(item.Model, key);
                    item.Status = result.Message;
                    if (result.Success) readyProviders++;
                }
                catch (Exception ex)
                {
                    item.Status = $"Unavailable: {ex.Message}";
                }
            }

            UpdateAiAutomationReadiness(readyProviders, enabledProviders.Length);
            StatusMessage = AiAutomationReadiness;
        }
        finally { IsBusy = false; }
    }

    private void UpdateAiAutomationReadiness(int? readyProviders = null, int? enabledProviders = null)
    {
        var enabled = enabledProviders ?? AiProviders.Count(x => x.Enabled);
        var ready = readyProviders ?? AiProviders.Count(x => x.Enabled &&
            (x.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) || x.HasStoredApiKey || !string.IsNullOrWhiteSpace(x.ApiKey)));

        var blockers = new List<string>();
        if (!EnableAiRecommendations) blockers.Add("AI recommendations are disabled");
        if (enabled == 0) blockers.Add("no AI provider is enabled");
        if (ready == 0) blockers.Add("no enabled provider has a usable credential or local endpoint");
        if (AutoExecuteLowRiskAiActions && !RequireVerifiedExecutionResult) blockers.Add("automatic execution requires post-write verification");
        if (AutoExecuteLowRiskAiActions && !CaptureBeforeAfterEvidence) blockers.Add("before/after evidence is disabled");
        if (!AutoRejectHighRiskAiActions && ErrorDecisionMode == "AutoLowRisk") blockers.Add("high-risk auto-rejection is disabled");

        if (blockers.Count == 0)
        {
            AiAutomationReadiness = $"READY • {ready} of {enabled} enabled provider(s) available. Low-risk execution is protected by verification and evidence capture.";
        }
        else
        {
            AiAutomationReadiness = "ATTENTION • " + string.Join("; ", blockers) + ".";
        }

        AiAutomationSafetySummary = $"Decision mode: {ErrorDecisionMode}. Auto-execute low risk: {(AutoExecuteLowRiskAiActions ? "On" : "Off")}. Auto-reject high risk: {(AutoRejectHighRiskAiActions ? "On" : "Off")}. Evidence: {(CaptureBeforeAfterEvidence ? "On" : "Off")}. Verification: {(RequireVerifiedExecutionResult ? "On" : "Off")}.";
    }

}
