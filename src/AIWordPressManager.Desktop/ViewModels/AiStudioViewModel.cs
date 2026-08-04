using System.Collections.ObjectModel;
using System.Diagnostics;
using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Application.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class AiStudioViewModel : ObservableObject
{
    private readonly IApplicationSettingsService _settingsService;
    private readonly ISecretProtectionService _secretProtection;
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers;
    private readonly global::AIWordPressManager.Desktop.Services.UiOperationService _operations;

    public ObservableCollection<string> Providers { get; } = [];
    public IAsyncRelayCommand RunCommand { get; }
    public IAsyncRelayCommand LoadCommand { get; }

    [ObservableProperty] private string? _selectedProvider;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private string _taskInstruction = "Create an exact, safe SEO improvement.";
    [ObservableProperty] private string _currentValue = string.Empty;
    [ObservableProperty] private string _desiredOutcome = string.Empty;
    [ObservableProperty] private string _response = "Run a provider test to see the exact AI proposal here.";
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string _metrics = "Ready";
    [ObservableProperty] private bool _isBusy;

    public AiStudioViewModel(
        IApplicationSettingsService settingsService,
        ISecretProtectionService secretProtection,
        IEnumerable<IAiProvider> providers,
        global::AIWordPressManager.Desktop.Services.UiOperationService operations)
    {
        _settingsService = settingsService;
        _secretProtection = secretProtection;
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _operations = operations;
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedProvider));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
    }

    partial void OnIsBusyChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(string? value)
    {
        RunCommand.NotifyCanExecuteChanged();
        _ = LoadSelectedProviderDefaultsAsync(value);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        _operations.Start("AI Studio", "Loading providers", "Reading enabled providers from SQLite", 15);
        try
        {
            _operations.Report(25, "Loading provider settings", "Reading encrypted provider configuration");
            var settings = await _settingsService.GetAiSettingsAsync();
            Providers.Clear();
            foreach (var provider in settings.Providers.Where(x => x.Enabled).OrderBy(x => x.Priority))
                Providers.Add(provider.Provider);
            SelectedProvider ??= Providers.FirstOrDefault();
            if (SelectedProvider is not null)
                await LoadSelectedProviderDefaultsAsync(SelectedProvider);
            Metrics = Providers.Count == 0 ? "No enabled AI provider. Enable one in Settings." : $"{Providers.Count} enabled provider(s) loaded.";
            _operations.Complete(Metrics);
        }
        finally { IsBusy = false; await Task.Delay(350); _operations.Hide(); }
    }

    private async Task LoadSelectedProviderDefaultsAsync(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)) return;
        var settings = await _settingsService.GetAiSettingsAsync();
        var selected = settings.Providers.FirstOrDefault(x => x.Provider.Equals(providerName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) Model = selected.Model;
    }

    private async Task RunAsync()
    {
        if (SelectedProvider is null || !_providers.TryGetValue(SelectedProvider, out var provider)) return;
        IsBusy = true;
        _operations.Start("AI Studio", "Preparing request", $"Using {SelectedProvider}", 10);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var settings = await _settingsService.GetAiSettingsAsync();
            var selected = settings.Providers.FirstOrDefault(x => x.Provider.Equals(SelectedProvider, StringComparison.OrdinalIgnoreCase));
            if (selected is null) throw new InvalidOperationException("The selected provider is not configured.");

            var key = string.Empty;
            if (!SelectedProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(selected.ProtectedApiKey))
                    throw new InvalidOperationException($"{SelectedProvider} does not have a stored credential. Configure it in Settings first.");
                key = await _secretProtection.UnprotectAsync(selected.ProtectedApiKey);
            }

            var input = new AiSuggestionInput(
                "AI Studio",
                "Preview",
                "studio-preview",
                "GenerateExactProposal",
                CurrentValue,
                DesiredOutcome,
                $"Task: {TaskInstruction}. Return a concrete replacement value, not instructions.",
                "Low");

            _operations.Report(55, "Generating proposal", $"Waiting for {SelectedProvider} response");
            var results = await provider.ImproveSuggestionsAsync([input], string.IsNullOrWhiteSpace(Model) ? selected.Model : Model, key);
            var result = results.FirstOrDefault();
            Response = result?.ProposedValue ?? "The provider returned no proposal.";
            Reason = result?.Reason ?? string.Empty;
            Metrics = $"Provider: {SelectedProvider} • Model: {Model} • {stopwatch.ElapsedMilliseconds:N0} ms • Confidence: {(result?.Confidence ?? 0):P0}";
            _operations.Complete("Exact proposal generated successfully");
        }
        catch (Exception ex)
        {
            Response = "Request failed.";
            Reason = ex.Message;
            Metrics = $"Failed after {stopwatch.ElapsedMilliseconds:N0} ms";
            _operations.Fail(ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            IsBusy = false;
            await Task.Delay(700);
            _operations.Hide();
        }
    }
}
