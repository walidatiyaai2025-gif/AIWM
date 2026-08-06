using System.ComponentModel;
using System.IO;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _executionReceiptBindingInitialized;

    public void BindExecutionReceiptStore()
    {
        if (_executionReceiptBindingInitialized)
            return;

        _executionReceiptBindingInitialized = true;
        ExecutionCenter.PropertyChanged += OnExecutionCenterReceiptChanged;
        SynchronizeExecutionReceiptFromCenter();
    }

    private void OnExecutionCenterReceiptChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ExecutionCenterViewModel.LatestReceiptPath) or
            nameof(ExecutionCenterViewModel.LatestReceiptStatus))
        {
            SynchronizeExecutionReceiptFromCenter();
        }
    }

    private void SynchronizeExecutionReceiptFromCenter()
    {
        var receiptPath = ExecutionCenter.LatestReceiptPath;
        LastOptimizationReceiptPath = receiptPath;

        LastOptimizationRunText = !string.IsNullOrWhiteSpace(receiptPath) && File.Exists(receiptPath)
            ? ExecutionCenter.LatestReceiptStatus
            : "No verified execution receipt is available yet";
    }

    partial void OnLastOptimizationReceiptPathChanged(string? value)
        => OpenLastOptimizationReceiptCommand.NotifyCanExecuteChanged();
}
