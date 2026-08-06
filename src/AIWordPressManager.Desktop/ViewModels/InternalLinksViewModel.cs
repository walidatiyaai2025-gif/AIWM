using System.Collections.ObjectModel;
using AIWordPressManager.Application.Planning;
using AIWordPressManager.Desktop.Services.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class InternalLinksViewModel : ObservableObject
{
    private readonly IInternalLinkSuggestionService _service;
    private readonly ICurrentSiteContext _siteContext;

    public ObservableCollection<InternalLinkSuggestionItem> Suggestions { get; } = [];
    public IAsyncRelayCommand GenerateCommand { get; }

    [ObservableProperty] private string _statusMessage = "Suggestions are generated locally from synchronized published content.";
    [ObservableProperty] private bool _isBusy;

    public InternalLinksViewModel(
        IInternalLinkSuggestionService service,
        ICurrentSiteContext siteContext)
    {
        _service = service;
        _siteContext = siteContext;
        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && _siteContext.HasSite);

        _siteContext.CurrentSiteChanged += (_, args) =>
        {
            Suggestions.Clear();
            StatusMessage = args.Current.HasSite
                ? $"{args.Current.SiteName} selected. Generate its internal-link suggestions."
                : "Select a site before generating internal-link suggestions.";
            GenerateCommand.NotifyCanExecuteChanged();
        };
    }

    partial void OnIsBusyChanged(bool value) => GenerateCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        if (_siteContext.HasSite) await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        var context = _siteContext.Capture();
        if (context.SiteId is not Guid siteId)
        {
            StatusMessage = "Select a site first.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Generating internal-link suggestions for {context.SiteName}…";
        try
        {
            var result = await _service.GenerateAsync(siteId);
            if (!_siteContext.IsCurrent(context)) return;

            Suggestions.Clear();
            foreach (var item in result) Suggestions.Add(item);
            StatusMessage = $"Generated {Suggestions.Count} offline suggestions for {context.SiteName}. Review and approval are required before execution.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
