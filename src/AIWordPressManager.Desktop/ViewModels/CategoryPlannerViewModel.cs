using System.Collections.ObjectModel;
using AIWordPressManager.Application.Planning;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class CategoryPlannerViewModel : ObservableObject
{
    private readonly ICategoryPlannerService _service; private readonly SitesViewModel _sites;
    public ObservableCollection<CategoryPlanItem> Items { get; } = [];
    public IAsyncRelayCommand AnalyzeCommand { get; }
    [ObservableProperty] private string _statusMessage = "Select a site. Category analysis uses the local SQLite snapshot.";
    [ObservableProperty] private int _emptyCategories; [ObservableProperty] private int _weakCategories; [ObservableProperty] private int _healthyCategories; [ObservableProperty] private bool _isBusy;
    public CategoryPlannerViewModel(ICategoryPlannerService service, SitesViewModel sites){_service=service;_sites=sites;AnalyzeCommand=new AsyncRelayCommand(AnalyzeAsync,()=>!IsBusy&&_sites.SelectedSite is not null);_sites.SelectedSiteChanged+=(_,_)=>AnalyzeCommand.NotifyCanExecuteChanged();}
    partial void OnIsBusyChanged(bool value)=>AnalyzeCommand.NotifyCanExecuteChanged();
    public async Task LoadAsync(){ if(_sites.SelectedSite is not null) await AnalyzeAsync(); }
    private async Task AnalyzeAsync(){var site=_sites.SelectedSite;if(site is null){StatusMessage="Select a site first.";return;}IsBusy=true;try{var result=await _service.AnalyzeAsync(site.Id);Items.Clear();foreach(var item in result.Items)Items.Add(item);EmptyCategories=result.EmptyCategories;WeakCategories=result.WeakCategories;HealthyCategories=result.HealthyCategories;StatusMessage=$"Offline analysis completed for {Items.Count} categories. No WordPress changes were made.";}finally{IsBusy=false;}}
}
