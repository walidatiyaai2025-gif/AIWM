using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.SiteBrain;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class SiteBrainViewModel : ObservableObject
{
    private readonly SitesViewModel _sites;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDialogService _dialogs;
    private readonly UiOperationService _operations;

    [ObservableProperty] private string _primaryLanguage = "Arabic";
    [ObservableProperty] private string _writingTone = "Professional";
    [ObservableProperty] private string _targetAudience = "General audience";
    [ObservableProperty] private string _preferredSeoPlugin = "Auto detect";
    [ObservableProperty] private string _preferredPageBuilder = "Auto detect";
    [ObservableProperty] private string _brandColors = "Black, white and readable gold";
    [ObservableProperty] private string _preferredImageSize = "1200x630";
    [ObservableProperty] private string _internalLinkStrategy = "Natural contextual links";
    [ObservableProperty] private string _categoryStrategy = "Clear parent and child categories";
    [ObservableProperty] private string _contentRules = "Factual, concise, no invented statistics";
    [ObservableProperty] private string _designRules = "Responsive, accessible, consistent spacing";
    [ObservableProperty] private string _rejectedPatterns = string.Empty;
    [ObservableProperty] private string _primaryGoal = "Increase organic traffic";
    [ObservableProperty] private string _targetKeywords = string.Empty;
    [ObservableProperty] private string _competitors = string.Empty;
    [ObservableProperty] private string _publishingSchedule = "2 articles per week";
    [ObservableProperty] private bool _autopilotEnabled;
    [ObservableProperty] private string _brainReadiness = "Not evaluated";
    [ObservableProperty] private string _todayMission = "Load a site memory profile to generate the mission.";
    [ObservableProperty] private string _statusMessage = "Select a site to load its AI memory.";
    [ObservableProperty] private string _lastUpdated = "Never";
    [ObservableProperty] private bool _isBusy;

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }

    public SiteBrainViewModel(SitesViewModel sites, IServiceScopeFactory scopeFactory, IDialogService dialogs, UiOperationService operations)
    {
        _sites = sites;
        _scopeFactory = scopeFactory;
        _dialogs = dialogs;
        _operations = operations;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && _sites.SelectedSite is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && _sites.SelectedSite is not null);
        _sites.SelectedSiteChanged += (_, _) => _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_sites.SelectedSite is null) return;
        IsBusy = true; NotifyCommands();
        _operations.Start("AI Site Brain", "Loading memory", "Reading the selected site's profile from SQLite", 20);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var profile = await scope.ServiceProvider.GetRequiredService<ISiteBrainService>().GetAsync(_sites.SelectedSite.Id);
            Apply(profile);
            StatusMessage = $"Loaded AI memory for {_sites.SelectedSite.Name}.";
            LastUpdated = profile.UpdatedAtUtc.ToLocalTime().ToString("g");
            _operations.Complete(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _operations.Fail(ex.Message);
            await _dialogs.ShowErrorAsync("AI Site Brain", ex.Message);
        }
        finally
        {
            IsBusy = false; NotifyCommands(); await Task.Delay(300); _operations.Hide();
        }
    }

    private async Task SaveAsync()
    {
        if (_sites.SelectedSite is null) return;
        IsBusy = true; NotifyCommands();
        _operations.Start("AI Site Brain", "Saving memory", "Persisting site-specific guidance to SQLite", 25);
        try
        {
            var profile = new SiteBrainProfile(_sites.SelectedSite.Id, PrimaryLanguage.Trim(), WritingTone.Trim(), TargetAudience.Trim(),
                PreferredSeoPlugin.Trim(), PreferredPageBuilder.Trim(), BrandColors.Trim(), PreferredImageSize.Trim(),
                InternalLinkStrategy.Trim(), CategoryStrategy.Trim(), ContentRules.Trim(), DesignRules.Trim(), RejectedPatterns.Trim(), DateTime.UtcNow,
                PrimaryGoal.Trim(), TargetKeywords.Trim(), Competitors.Trim(), PublishingSchedule.Trim(), AutopilotEnabled);
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISiteBrainService>().SaveAsync(profile);
            LastUpdated = DateTime.Now.ToString("g");
            StatusMessage = "AI memory saved. Future prompts can use these rules as site context.";
            _operations.Complete(StatusMessage);
            await _dialogs.ShowInformationAsync("AI Site Brain", StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message; _operations.Fail(ex.Message); await _dialogs.ShowErrorAsync("AI Site Brain", ex.Message);
        }
        finally
        {
            IsBusy = false; NotifyCommands(); await Task.Delay(300); _operations.Hide();
        }
    }

    private void Apply(SiteBrainProfile p)
    {
        PrimaryLanguage=p.PrimaryLanguage; WritingTone=p.WritingTone; TargetAudience=p.TargetAudience;
        PreferredSeoPlugin=p.PreferredSeoPlugin; PreferredPageBuilder=p.PreferredPageBuilder; BrandColors=p.BrandColors;
        PreferredImageSize=p.PreferredImageSize; InternalLinkStrategy=p.InternalLinkStrategy; CategoryStrategy=p.CategoryStrategy;
        ContentRules=p.ContentRules; DesignRules=p.DesignRules; RejectedPatterns=p.RejectedPatterns;
        PrimaryGoal=p.PrimaryGoal; TargetKeywords=p.TargetKeywords; Competitors=p.Competitors; PublishingSchedule=p.PublishingSchedule; AutopilotEnabled=p.AutopilotEnabled;
        RefreshBrainSummary();
    }

    private void RefreshBrainSummary()
    {
        var completed = new[]
        {
            PrimaryLanguage, WritingTone, TargetAudience, PrimaryGoal, TargetKeywords,
            InternalLinkStrategy, CategoryStrategy, ContentRules, DesignRules
        }.Count(value => !string.IsNullOrWhiteSpace(value));

        var percent = (int)Math.Round(completed / 9d * 100d);
        BrainReadiness = percent >= 90 ? $"READY • {percent}%" : $"NEEDS CONTEXT • {percent}%";
        var keyword = string.IsNullOrWhiteSpace(TargetKeywords) ? "the site's priority topics" : TargetKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? TargetKeywords;
        TodayMission = $"Goal: {PrimaryGoal}. Focus first on {keyword}. Publishing cadence: {PublishingSchedule}.";
    }

    private void NotifyCommands() { LoadCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged(); }
}
