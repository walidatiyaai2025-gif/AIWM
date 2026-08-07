using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIWordPressManager.Desktop.ViewModels
{
    public sealed partial class SeoAuditViewModel
    {
        public ObservableCollection<SeoAuditJourneyRequirement> FirstJourneyRequirements { get; } = [];

        private bool _isFirstJourneyReady;
        private string _firstJourneyStatus = "Run the first measurable SEO audit for the synchronized site.";
        private DateTimeOffset? _firstJourneyCompletedAt;

        public bool IsFirstJourneyReady
        {
            get => _isFirstJourneyReady;
            private set => SetProperty(ref _isFirstJourneyReady, value);
        }

        public string FirstJourneyStatus
        {
            get => _firstJourneyStatus;
            private set => SetProperty(ref _firstJourneyStatus, value);
        }

        public DateTimeOffset? FirstJourneyCompletedAt
        {
            get => _firstJourneyCompletedAt;
            private set => SetProperty(ref _firstJourneyCompletedAt, value);
        }

        internal void RefreshFirstJourneyReadiness()
        {
            var hasSite = _siteContext.HasSite;
            var hasAuditedItems = AuditedItems > 0;
            var hasValidScore = hasAuditedItems && Score is >= 0 and <= 100;
            var hasIssueClassification = HighIssues >= 0 && MediumIssues >= 0 && LowIssues >= 0 &&
                                         HighIssues + MediumIssues + LowIssues == Issues.Count;
            var hasSavedBaseline = History.Count > 0;

            FirstJourneyRequirements.Clear();
            FirstJourneyRequirements.Add(new SeoAuditJourneyRequirement("Synchronized site", "A current WordPress site is selected for this audit.", hasSite));
            FirstJourneyRequirements.Add(new SeoAuditJourneyRequirement("Audited content", $"{AuditedItems} WordPress item(s) were evaluated.", hasAuditedItems));
            FirstJourneyRequirements.Add(new SeoAuditJourneyRequirement("Measurable score", hasValidScore ? $"Baseline score: {Score}/100." : "Run the audit to calculate a score from 0 to 100.", hasValidScore));
            FirstJourneyRequirements.Add(new SeoAuditJourneyRequirement("Issue classification", $"High {HighIssues} · Medium {MediumIssues} · Low {LowIssues}.", hasIssueClassification));
            FirstJourneyRequirements.Add(new SeoAuditJourneyRequirement("Saved baseline", hasSavedBaseline ? $"{History.Count} audit history point(s) stored." : "The audit baseline must be stored in SQLite history.", hasSavedBaseline));

            IsFirstJourneyReady = hasSite && hasAuditedItems && hasValidScore && hasIssueClassification && hasSavedBaseline;
            if (IsFirstJourneyReady)
            {
                FirstJourneyCompletedAt ??= DateTimeOffset.Now;
                FirstJourneyStatus = $"SEO baseline ready: {Score}/100 with {Issues.Count} measurable issue(s).";
            }
            else if (!hasSite)
            {
                FirstJourneyStatus = "Select and synchronize a site before running the SEO audit.";
            }
            else if (IsRunning)
            {
                FirstJourneyStatus = "The measurable SEO baseline is being calculated.";
            }
            else if (!hasAuditedItems)
            {
                FirstJourneyStatus = "Run SEO Audit to evaluate the synchronized WordPress snapshot.";
            }
            else if (!hasSavedBaseline)
            {
                FirstJourneyStatus = "Save and load the audit history before continuing.";
            }
            else
            {
                FirstJourneyStatus = "Complete every SEO baseline requirement before reviewing suggested changes.";
            }
        }
    }

    public sealed record SeoAuditJourneyRequirement(string Title, string Description, bool IsCompleted)
    {
        public string StatusIcon => IsCompleted ? "✓" : "○";
    }
}
