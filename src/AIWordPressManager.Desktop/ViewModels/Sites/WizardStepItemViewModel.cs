using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.ViewModels.Sites;

public sealed partial class WizardStepItemViewModel(int number, string title) : ObservableObject
{
    public int Number { get; } = number;
    public string Title { get; } = title;
    [ObservableProperty] private string _statusMark = "○";
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isCompleted;
}
