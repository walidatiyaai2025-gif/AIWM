using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class WordPressExplorerViewModel
{
    public IAsyncRelayCommand SynchronizeNowCommand => RefreshCommand;
}
