using System.Collections.ObjectModel;
using AIWordPressManager.Application.Planning;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class InternalLinksViewModel : ObservableObject
{
    private readonly IInternalLinkSuggestionService _service; private readonly SitesViewModel _sites;
    public ObservableCollection<InternalLinkSuggestionItem> Suggestions { get; } = [];
    public IAsyncRelayCommand GenerateCommand { get; }
    [ObservableProperty] private string _statusMessage = "Suggestions are generated locally from synchronized published content.";
    [ObservableProperty] private bool _isBusy;
    public InternalLinksViewModel(IInternalLinkSuggestionService service,SitesViewModel sites){_service=service;_sites=sites;GenerateCommand=new AsyncRelayCommand(GenerateAsync,()=>!IsBusy&&_sites.SelectedSite is not null);_sites.SelectedSiteChanged+=(_,_)=>GenerateCommand.NotifyCanExecuteChanged();}
    partial void OnIsBusyChanged(bool value)=>GenerateCommand.NotifyCanExecuteChanged();
    public async Task LoadAsync(){if(_sites.SelectedSite is not null)await GenerateAsync();}
    private async Task GenerateAsync(){var site=_sites.SelectedSite;if(site is null){StatusMessage="Select a site first.";return;}IsBusy=true;try{var result=await _service.GenerateAsync(site.Id);Suggestions.Clear();foreach(var item in result)Suggestions.Add(item);StatusMessage=$"Generated {Suggestions.Count} offline suggestions. Review and approval will be required before future execution.";}finally{IsBusy=false;}}
}
