using AIWordPressManager.Application.Abstractions;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ThemeInspectorViewModel : ObservableObject
{
    private readonly IWordPressThemeService _service;
    private readonly IThemeIntelligenceStore _store;
    private readonly SitesViewModel _sites;
    private readonly UiOperationService _operations;
    private readonly IDialogService _dialogs;

    public IAsyncRelayCommand DiscoverCommand { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Select a site, then discover the active theme.";
    [ObservableProperty] private string _themeName = "Not detected";
    [ObservableProperty] private string _stylesheet = "";
    [ObservableProperty] private string _template = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _discoveryMethod = "";
    [ObservableProperty] private string _capabilities = "";
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _themeFamily = "Unknown";
    [ObservableProperty] private string _recommendedAdapter = "Generic WordPress adapter";
    [ObservableProperty] private string _safeChangeStrategy = "Use approved Custom CSS or a child theme on staging.";
    [ObservableProperty] private string _riskSummary = "Live theme changes are disabled until discovery is complete.";
    [ObservableProperty] private string _lastDiscoveryText = "Never";

    public ThemeInspectorViewModel(
        IWordPressThemeService service,
        IThemeIntelligenceStore store,
        SitesViewModel sites,
        UiOperationService operations,
        IDialogService dialogs)
    {
        _service = service;
        _store = store;
        _sites = sites;
        _operations = operations;
        _dialogs = dialogs;
        DiscoverCommand = new AsyncRelayCommand(DiscoverAsync, () => !IsBusy && _sites.SelectedSite is not null);
        _sites.SelectedSiteChanged += async (_, _) =>
        {
            DiscoverCommand.NotifyCanExecuteChanged();
            await LoadOfflineAsync();
        };
    }

    partial void OnIsBusyChanged(bool value) => DiscoverCommand.NotifyCanExecuteChanged();

    public async Task LoadOfflineAsync()
    {
        if (_sites.SelectedSite is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        var cached = await _store.GetAsync(_sites.SelectedSite.Id);
        if (cached is null)
        {
            ResetDisplay();
            StatusMessage = "No saved theme intelligence is available. Click Discover theme for a live, read-only check.";
            return;
        }

        Apply(cached);
        StatusMessage = $"Loaded saved theme intelligence for {_sites.SelectedSite.Name} from SQLite.";
    }

    public async Task DiscoverAsync()
    {
        var site = _sites.SelectedSite;
        if (site is null)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        IsBusy = true;
        _operations.Start("Theme Intelligence", "Discovering theme", "Reading WordPress theme metadata and capabilities", 10);
        try
        {
            var result = await _service.DiscoverAsync(site.Id);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                _operations.Fail(StatusMessage);
                await _dialogs.ShowErrorAsync("Theme Intelligence", StatusMessage);
                return;
            }

            _operations.Report(65, "Classifying theme", "Selecting the safest adapter and change strategy");
            var profile = BuildProfile(site.Id, result.Value);
            await _store.SaveAsync(profile);
            Apply(profile);
            StatusMessage = $"Theme intelligence saved at {DateTime.Now:g}.";
            _operations.Complete(StatusMessage);
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            _operations.Fail(exception.Message);
            await _dialogs.ShowErrorAsync("Theme Intelligence", exception.Message);
        }
        finally
        {
            IsBusy = false;
            await Task.Delay(500);
            _operations.Hide();
        }
    }

    private static ThemeIntelligenceProfile BuildProfile(Guid siteId, WordPressThemeDiscoveryResult result)
    {
        var theme = result.ActiveTheme;
        var name = theme?.Name ?? "Not detected";
        var slug = (theme?.Stylesheet ?? name).Trim().ToLowerInvariant();
        var family = DetectFamily(slug, name);
        var adapter = family switch
        {
            "Elementor / Hello" => "Elementor adapter + generated CSS regeneration",
            "Divi" => "Divi adapter with staging-only layout changes",
            "Astra" or "Kadence" or "GeneratePress" or "Blocksy" => "Customizer/theme-options adapter + child-theme CSS",
            "Block theme" => "Gutenberg Site Editor / theme.json adapter",
            _ => "Generic WordPress theme adapter"
        };
        var strategy = theme?.IsBlockTheme == true
            ? "Prefer theme.json, Global Styles and block templates. Backup before changing templates."
            : "Prefer Custom CSS or a child theme. Never edit the parent theme directly.";
        var risk = theme is null
            ? "High uncertainty: theme identity is unavailable; keep all design changes in preview mode."
            : "Low risk for read-only analysis; CSS changes require approval, backup and visual verification. PHP/template edits remain high risk.";

        return new ThemeIntelligenceProfile(
            siteId, name, theme?.Stylesheet ?? "", theme?.Template ?? "", theme?.Version ?? "",
            theme?.Author ?? "", theme?.IsBlockTheme ?? false, family, adapter, strategy, risk,
            string.Join(", ", result.DetectedCapabilities.DefaultIfEmpty("No extra REST capabilities detected")),
            result.DiscoveryMethod, result.Notes, DateTime.UtcNow);
    }

    private static string DetectFamily(string slug, string name)
    {
        var value = $"{slug} {name}".ToLowerInvariant();
        if (value.Contains("hello") || value.Contains("elementor")) return "Elementor / Hello";
        if (value.Contains("astra")) return "Astra";
        if (value.Contains("kadence")) return "Kadence";
        if (value.Contains("generatepress")) return "GeneratePress";
        if (value.Contains("blocksy")) return "Blocksy";
        if (value.Contains("divi")) return "Divi";
        if (value.Contains("avada")) return "Avada";
        if (value.Contains("woodmart")) return "WoodMart";
        return value.Contains("twenty twenty") ? "Block theme" : "Custom / other";
    }

    private void Apply(ThemeIntelligenceProfile profile)
    {
        ThemeName = profile.ThemeName;
        Stylesheet = profile.Stylesheet;
        Template = profile.Template;
        Version = profile.Version;
        Author = profile.Author;
        ThemeFamily = profile.ThemeFamily;
        RecommendedAdapter = profile.RecommendedAdapter;
        SafeChangeStrategy = profile.SafeChangeStrategy;
        RiskSummary = profile.RiskSummary;
        Capabilities = profile.Capabilities;
        DiscoveryMethod = profile.DiscoveryMethod;
        Notes = profile.Notes;
        LastDiscoveryText = profile.UpdatedAtUtc.ToLocalTime().ToString("g");
    }

    private void ResetDisplay()
    {
        ThemeName = "Not detected";
        Stylesheet = Template = Version = Author = DiscoveryMethod = Capabilities = Notes = "";
        ThemeFamily = "Unknown";
        RecommendedAdapter = "Generic WordPress adapter";
        SafeChangeStrategy = "Use approved Custom CSS or a child theme on staging.";
        RiskSummary = "Live theme changes are disabled until discovery is complete.";
        LastDiscoveryText = "Never";
    }
}
