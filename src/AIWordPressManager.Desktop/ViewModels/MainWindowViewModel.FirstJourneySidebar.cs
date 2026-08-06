using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<FirstJourneyPage> FirstJourneySidebarPages { get; } = [];

    private string _firstJourneySidebarSummary = "Start with Dashboard, then complete each required page in order.";
    public string FirstJourneySidebarSummary
    {
        get => _firstJourneySidebarSummary;
        private set => SetProperty(ref _firstJourneySidebarSummary, value);
    }

    internal void RefreshFirstJourneySidebar()
    {
        var completionByTarget = CompleteJourneySteps
            .GroupBy(step => step.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.All(step => step.IsCompleted), StringComparer.OrdinalIgnoreCase);

        var currentTarget = !Sites.IsFirstJourneyReady
            ? "Sites"
            : !Explorer.IsFirstJourneyReady
                ? "WordPress Explorer"
                : !SeoAudit.IsFirstJourneyReady
                    ? "SEO Audit"
                    : !SuggestedChanges.IsFirstJourneyReady
                        ? "Suggested Changes"
                        : !SuggestedChanges.IsApprovalJourneyReady
                            ? "Approval Queue"
                            : CompleteJourneySteps.FirstOrDefault(step => step.IsCurrent)?.Target ?? "Execution Center";

        var definitions = new[]
        {
            new FirstJourneyDefinition("0", "Dashboard", "Journey overview and next required action."),
            new FirstJourneyDefinition("1", "Sites", "Register, test and select the WordPress site."),
            new FirstJourneyDefinition("2", "WordPress Explorer", "Synchronize and verify the local WordPress snapshot."),
            new FirstJourneyDefinition("3", "SEO Audit", "Build the first SEO and technical baseline."),
            new FirstJourneyDefinition("4", "Suggested Changes", "Review recommendations and preview proposed values."),
            new FirstJourneyDefinition("5", "Approval Queue", "Approve only safe changes for execution."),
            new FirstJourneyDefinition("6", "Execution Center", "Back up, execute and preserve the audit receipt."),
            new FirstJourneyDefinition("7", "Evidence Center", "Verify before/after values and rollback evidence.")
        };

        FirstJourneySidebarPages.Clear();
        foreach (var definition in definitions)
        {
            var isDashboard = definition.Target.Equals("Dashboard", StringComparison.OrdinalIgnoreCase);
            var completed = definition.Target switch
            {
                "Dashboard" => CompleteJourneySteps.Count > 0,
                "Sites" => Sites.IsFirstJourneyReady,
                "WordPress Explorer" => Explorer.IsFirstJourneyReady,
                "SEO Audit" => SeoAudit.IsFirstJourneyReady,
                "Suggested Changes" => SuggestedChanges.IsFirstJourneyReady,
                "Approval Queue" => SuggestedChanges.IsApprovalJourneyReady,
                _ => completionByTarget.TryGetValue(definition.Target, out var targetCompleted) && targetCompleted
            };
            var current = isDashboard
                ? CurrentPage.Equals("Dashboard", StringComparison.OrdinalIgnoreCase)
                : definition.Target.Equals(currentTarget, StringComparison.OrdinalIgnoreCase);

            FirstJourneySidebarPages.Add(new FirstJourneyPage(
                definition.Number,
                definition.Target,
                definition.Description,
                completed,
                current,
                NavigateCommand));
        }

        var completedPages = FirstJourneySidebarPages.Count(page => page.IsCompleted);
        FirstJourneySidebarSummary = completedPages == FirstJourneySidebarPages.Count
            ? "First journey completed and verified."
            : $"{completedPages} of {FirstJourneySidebarPages.Count} pages ready.";
    }

    private sealed record FirstJourneyDefinition(string Number, string Target, string Description);
}

public sealed record FirstJourneyPage(
    string Number,
    string Target,
    string Description,
    bool IsCompleted,
    bool IsCurrent,
    ICommand NavigateCommand)
{
    public string DisplayTitle => $"{Number}. {Target}";
    public string StatusIcon => IsCompleted ? "✓" : IsCurrent ? "▶" : "○";
    public Brush StatusBrush => IsCompleted ? Brushes.SeaGreen : IsCurrent ? Brushes.DodgerBlue : Brushes.SlateGray;
}
