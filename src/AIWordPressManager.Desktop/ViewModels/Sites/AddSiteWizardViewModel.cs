using System.Collections.ObjectModel;
using AIWordPressManager.Application.Abstractions.WordPress;
using AIWordPressManager.Application.Sites;
using AIWordPressManager.Desktop.Validators;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;

namespace AIWordPressManager.Desktop.ViewModels.Sites;

public sealed partial class AddSiteWizardViewModel : ObservableObject
{
    private static readonly string[] StepNames =
    [
        "Website information",
        "WordPress credentials",
        "Test and discover",
        "Review and start synchronization"
    ];

    private readonly IWordPressConnectionTester _connectionTester;
    private readonly ISiteManagementService _siteManagementService;
    private readonly AddSiteWizardValidator _validator;
    private WordPressConnectionResult? _connectionResult;

    public ObservableCollection<WizardStepItemViewModel> Steps { get; } = [];
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand NextCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand ShowDiagnosticsCommand { get; }
    public IRelayCommand CloseDiagnosticsCommand { get; }

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _stepIndex;
    [ObservableProperty] private string _siteName = string.Empty;
    [ObservableProperty] private string _siteUrl = "https://";
    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty] private string _applicationPassword = string.Empty;
    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private string _connectionMessage = "Connection has not been tested.";
    [ObservableProperty] private bool _isConnectionSuccessful;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _enableAiFeatures = true;
    [ObservableProperty] private bool _useStagingForHighRiskChanges = true;
    [ObservableProperty] private bool _allowDraftCreation = true;
    [ObservableProperty] private bool _allowAutomaticPublishing;
    [ObservableProperty] private string _stagingUrl = string.Empty;
    [ObservableProperty] private string _discoverySummary = "Run the connection test to discover the WordPress website.";
    [ObservableProperty] private bool _isDiagnosticsOpen;
    [ObservableProperty] private string _diagnosticsTitle = "WordPress connection diagnostics";
    [ObservableProperty] private string _diagnosticsText = "No connection test has been run yet.";

    public string StepTitle => StepNames[StepIndex];
    public string StepCounter => $"Step {StepIndex + 1} of {StepNames.Length}";
    public double StepProgress => ((StepIndex + 1d) / StepNames.Length) * 100d;
    public bool IsFirstStep => StepIndex == 0;
    public bool IsLastStep => StepIndex == StepNames.Length - 1;
    public bool HasCapturedPassword => !string.IsNullOrWhiteSpace(ApplicationPassword);
    public string SaveButtonText => "Save & start first sync";

    public bool ShowBasicStep => StepIndex == 0;
    public bool ShowAuthenticationStep => StepIndex == 1;
    public bool ShowConnectionStep => StepIndex == 2;
    public bool ShowDiscoveryStep => false;
    public bool ShowSeoStep => false;
    public bool ShowBuilderStep => false;
    public bool ShowWooCommerceStep => false;
    public bool ShowAiStep => false;
    public bool ShowStagingStep => false;
    public bool ShowPermissionsStep => false;
    public bool ShowSummaryStep => false;
    public bool ShowReviewStep => StepIndex == 3;

    public event EventHandler? SiteSaved;

    public AddSiteWizardViewModel(
        IWordPressConnectionTester connectionTester,
        ISiteManagementService siteManagementService,
        AddSiteWizardValidator validator)
    {
        _connectionTester = connectionTester;
        _siteManagementService = siteManagementService;
        _validator = validator;

        for (var index = 0; index < StepNames.Length; index++)
            Steps.Add(new WizardStepItemViewModel(index + 1, StepNames[index]));

        CancelCommand = new RelayCommand(Cancel);
        BackCommand = new RelayCommand(Back, () => !IsFirstStep && !IsBusy);
        NextCommand = new AsyncRelayCommand(NextAsync, () => !IsLastStep && !IsBusy);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsLastStep && !IsBusy);
        ShowDiagnosticsCommand = new RelayCommand(() => IsDiagnosticsOpen = true);
        CloseDiagnosticsCommand = new RelayCommand(() => IsDiagnosticsOpen = false);
        RefreshSteps();
    }

    public void Open()
    {
        Reset();
        IsOpen = true;
    }

    private void Cancel() => IsOpen = false;

    private void Back()
    {
        ValidationMessage = string.Empty;
        if (StepIndex > 0)
            StepIndex--;
    }

    private async Task NextAsync()
    {
        ValidationMessage = string.Empty;

        if (StepIndex == 0)
        {
            if (string.IsNullOrWhiteSpace(SiteName))
            {
                ValidationMessage = "Site name is required.";
                return;
            }

            if (!Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                ValidationMessage = "Enter a valid HTTP or HTTPS URL.";
                return;
            }

            var duplicate = await FindDuplicateAsync();
            if (duplicate is not null)
            {
                ShowDuplicateMessage(duplicate.Name, duplicate.SiteUrl);
                return;
            }
        }
        else if (StepIndex == 1)
        {
            if (string.IsNullOrWhiteSpace(UserName))
            {
                ValidationMessage = "WordPress username is required.";
                return;
            }

            if (NormalizePassword(ApplicationPassword).Length < 8)
            {
                ValidationMessage = "Enter a valid WordPress Application Password.";
                return;
            }
        }
        else if (StepIndex == 2 && !IsConnectionSuccessful)
        {
            ValidationMessage = "Test the WordPress connection successfully before continuing.";
            return;
        }

        if (StepIndex < StepNames.Length - 1)
            StepIndex++;
    }

    private async Task TestConnectionAsync()
    {
        ValidationMessage = string.Empty;
        var validation = ValidateInput();
        if (!validation.IsValid)
        {
            ValidationMessage = validation.Errors.FirstOrDefault()?.ErrorMessage ?? "Complete the required fields.";
            return;
        }

        var duplicate = await FindDuplicateAsync();
        if (duplicate is not null)
        {
            ShowDuplicateMessage(duplicate.Name, duplicate.SiteUrl);
            return;
        }

        SetBusy(true);
        ConnectionMessage = "Testing WordPress REST API and credentials…";
        try
        {
            _connectionResult = await _connectionTester.TestAsync(
                new WordPressConnectionRequest(SiteUrl, UserName, NormalizePassword(ApplicationPassword)));

            IsConnectionSuccessful = _connectionResult.IsSuccess;
            ConnectionMessage = _connectionResult.Message;
            DiagnosticsTitle = _connectionResult.IsSuccess
                ? "WordPress connection succeeded"
                : "WordPress connection failed";
            DiagnosticsText = string.IsNullOrWhiteSpace(_connectionResult.Diagnostics)
                ? _connectionResult.Message
                : _connectionResult.Diagnostics;
            IsDiagnosticsOpen = true;

            if (_connectionResult.IsSuccess)
            {
                if (string.IsNullOrWhiteSpace(SiteName) && !string.IsNullOrWhiteSpace(_connectionResult.SiteName))
                    SiteName = _connectionResult.SiteName;

                DiscoverySummary = BuildDiscoverySummary(_connectionResult);
            }
        }
        catch (Exception ex)
        {
            IsConnectionSuccessful = false;
            ConnectionMessage = "Connection test failed: " + ex.Message;
            DiagnosticsTitle = "Unexpected connection test error";
            DiagnosticsText = $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
            IsDiagnosticsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        ValidationMessage = string.Empty;
        if (!IsConnectionSuccessful || _connectionResult is null)
        {
            ValidationMessage = "A successful connection test is required.";
            return;
        }

        SetBusy(true);
        try
        {
            var duplicate = await FindDuplicateAsync();
            if (duplicate is not null)
            {
                ShowDuplicateMessage(duplicate.Name, duplicate.SiteUrl);
                return;
            }

            var result = await _siteManagementService.CreateAsync(new CreateSiteRequest(
                SiteName,
                NormalizeUrl(SiteUrl),
                UserName,
                NormalizePassword(ApplicationPassword),
                _connectionResult.HomeUrl,
                _connectionResult.WordPressVersion,
                _connectionResult.LanguageCode));

            if (result.IsFailure)
            {
                ValidationMessage = IsDuplicateError(result.Error.Message)
                    ? BuildDuplicateMessage(SiteName, SiteUrl)
                    : result.Error.Message;
                return;
            }

            IsOpen = false;
            SiteSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (IsDuplicateError(ex.ToString()))
        {
            ValidationMessage = BuildDuplicateMessage(SiteName, SiteUrl);
        }
        catch (Exception ex)
        {
            ValidationMessage = "The website could not be saved. No WordPress content was changed. Details: " + GetUsefulMessage(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<dynamic?> FindDuplicateAsync()
    {
        var normalized = NormalizeUrl(SiteUrl);
        var sites = await _siteManagementService.GetSitesAsync();
        return sites.FirstOrDefault(site =>
            string.Equals(NormalizeUrl(site.SiteUrl), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowDuplicateMessage(string name, string url)
    {
        IsConnectionSuccessful = false;
        ValidationMessage = BuildDuplicateMessage(name, url);
        ConnectionMessage = "This website is already registered. Select its existing card to continue.";
        DiagnosticsTitle = "Website already registered";
        DiagnosticsText = ValidationMessage;
        IsDiagnosticsOpen = true;
    }

    private static string BuildDuplicateMessage(string name, string url) =>
        $"This website is already registered.{Environment.NewLine}{Environment.NewLine}" +
        $"Website: {name}{Environment.NewLine}" +
        $"URL: {NormalizeUrl(url)}{Environment.NewLine}{Environment.NewLine}" +
        "No duplicate website was created. Close this wizard, select the existing website card, then use right-click to synchronize, retest, open WordPress admin, or remove it.";

    private static bool IsDuplicateError(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("UNIQUE constraint failed: Sites.SiteUrl", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("already registered", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

    private static string GetUsefulMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
            current = current.InnerException;
        return current.Message;
    }

    partial void OnApplicationPasswordChanged(string value)
    {
        OnPropertyChanged(nameof(HasCapturedPassword));
        if (NormalizePassword(value).Length >= 8 && ValidationMessage.Contains("Application Password", StringComparison.OrdinalIgnoreCase))
            ValidationMessage = string.Empty;
        NotifyCommands();
    }

    partial void OnUserNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && ValidationMessage.Contains("username", StringComparison.OrdinalIgnoreCase))
            ValidationMessage = string.Empty;
    }

    partial void OnSiteNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && ValidationMessage.Contains("Site name", StringComparison.OrdinalIgnoreCase))
            ValidationMessage = string.Empty;
    }

    partial void OnSiteUrlChanged(string value)
    {
        IsConnectionSuccessful = false;
        _connectionResult = null;
        if (Uri.TryCreate(value, UriKind.Absolute, out _) && ValidationMessage.Contains("URL", StringComparison.OrdinalIgnoreCase))
            ValidationMessage = string.Empty;
    }

    partial void OnStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepCounter));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(ShowBasicStep));
        OnPropertyChanged(nameof(ShowAuthenticationStep));
        OnPropertyChanged(nameof(ShowConnectionStep));
        OnPropertyChanged(nameof(ShowDiscoveryStep));
        OnPropertyChanged(nameof(ShowSeoStep));
        OnPropertyChanged(nameof(ShowBuilderStep));
        OnPropertyChanged(nameof(ShowWooCommerceStep));
        OnPropertyChanged(nameof(ShowAiStep));
        OnPropertyChanged(nameof(ShowStagingStep));
        OnPropertyChanged(nameof(ShowPermissionsStep));
        OnPropertyChanged(nameof(ShowSummaryStep));
        OnPropertyChanged(nameof(ShowReviewStep));
        RefreshSteps();
        NotifyCommands();
    }

    private FluentValidation.Results.ValidationResult ValidateInput()
    {
        var input = new AddSiteWizardInput(SiteName, SiteUrl, UserName, NormalizePassword(ApplicationPassword));
        return ((IValidator<AddSiteWizardInput>)_validator).Validate(input);
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        BackCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        TestConnectionCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSteps()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].IsCurrent = index == StepIndex;
            Steps[index].IsCompleted = index < StepIndex;
            Steps[index].StatusMark = index < StepIndex ? "✓" : index == StepIndex ? "●" : "○";
        }
    }

    private void Reset()
    {
        StepIndex = 0;
        SiteName = string.Empty;
        SiteUrl = "https://";
        UserName = string.Empty;
        ApplicationPassword = string.Empty;
        ValidationMessage = string.Empty;
        ConnectionMessage = "Connection has not been tested.";
        DiscoverySummary = "Run the connection test to discover the WordPress website.";
        IsConnectionSuccessful = false;
        IsBusy = false;
        EnableAiFeatures = true;
        UseStagingForHighRiskChanges = true;
        AllowDraftCreation = true;
        AllowAutomaticPublishing = false;
        StagingUrl = string.Empty;
        _connectionResult = null;
        IsDiagnosticsOpen = false;
        DiagnosticsTitle = "WordPress connection diagnostics";
        DiagnosticsText = "No connection test has been run yet.";
        RefreshSteps();
        NotifyCommands();
    }

    private static string NormalizePassword(string value) =>
        (value ?? string.Empty).Replace(" ", string.Empty).Trim();

    private static string NormalizeUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
            return (value ?? string.Empty).Trim().TrimEnd('/');

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
            Host = uri.Host.ToLowerInvariant()
        };

        if ((builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && builder.Port == 443) ||
            (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && builder.Port == 80))
            builder.Port = -1;

        return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string BuildDiscoverySummary(WordPressConnectionResult result)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.SiteName)) values.Add($"Site: {result.SiteName}");
        if (!string.IsNullOrWhiteSpace(result.HomeUrl)) values.Add($"Home: {result.HomeUrl}");
        if (!string.IsNullOrWhiteSpace(result.WordPressVersion)) values.Add($"WordPress: {result.WordPressVersion}");
        if (!string.IsNullOrWhiteSpace(result.LanguageCode)) values.Add($"Language: {result.LanguageCode}");
        if (result.CurrentUserId.HasValue) values.Add($"Authenticated user ID: {result.CurrentUserId.Value}");
        return values.Count == 0
            ? "Connection succeeded. The initial synchronization will discover the remaining website data."
            : string.Join(Environment.NewLine, values);
    }
}
