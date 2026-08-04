using System.Collections.ObjectModel;
using System.Text;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed record ContentPlanRow(
    int Priority,
    string Topic,
    string ContentType,
    string SuggestedTitle,
    string PrimaryKeyword,
    string SearchIntent,
    string SeoRequirements,
    string Outline,
    string MetaDescription,
    string Status,
    string Reason,
    DateTime TargetDate,
    bool IsAiEnhanced);

public sealed partial class ContentPlannerViewModel : ObservableObject
{
    private readonly WordPressExplorerViewModel _explorer;
    private readonly ContentAuditViewModel _audit;
    private readonly SitesViewModel _sites;
    private readonly IAiSuggestionProvider _aiSuggestions;

    public ObservableCollection<ContentPlanRow> Items { get; } = [];
    public IAsyncRelayCommand GenerateCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand PrepareExecutionPreviewCommand { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Select a site. The planner uses the local SQLite snapshot and prepares execution-ready AI content briefs.";
    [ObservableProperty] private int _plannedCount;
    [ObservableProperty] private int _highPriorityCount;
    [ObservableProperty] private int _aiEnhancedCount;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private ContentPlanRow? _selectedItem;
    [ObservableProperty] private string _previewTitle = "Select a plan item";
    [ObservableProperty] private string _previewKeyword = "The primary keyword will appear here.";
    [ObservableProperty] private string _previewIntent = "Search intent will appear here.";
    [ObservableProperty] private string _previewMeta = "The proposed meta description will appear here.";
    [ObservableProperty] private string _previewOutline = "The exact article structure will appear here.";
    [ObservableProperty] private string _previewRequirements = "SEO requirements will appear here.";
    [ObservableProperty] private string _executionPreviewStatus = "Preview only — nothing has been written to WordPress.";

    public ContentPlannerViewModel(
        WordPressExplorerViewModel explorer,
        ContentAuditViewModel audit,
        SitesViewModel sites,
        IAiSuggestionProvider aiSuggestions)
    {
        _explorer = explorer;
        _audit = audit;
        _sites = sites;
        _aiSuggestions = aiSuggestions;

        GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => !IsBusy && _sites.SelectedSite is not null);
        ClearCommand = new RelayCommand(Clear);
        PrepareExecutionPreviewCommand = new RelayCommand(PrepareExecutionPreview, () => SelectedItem is not null);
        _sites.SelectedSiteChanged += (_, _) => GenerateCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => GenerateCommand.NotifyCanExecuteChanged();

    partial void OnSelectedItemChanged(ContentPlanRow? value)
    {
        PrepareExecutionPreviewCommand.NotifyCanExecuteChanged();
        UpdatePreview(value);
    }

    public async Task LoadAsync()
    {
        if (_sites.SelectedSite is not null && Items.Count == 0)
            await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        IsBusy = true;
        try
        {
            await _explorer.LoadAsync();
            await _audit.LoadAsync();

            Items.Clear();
            SelectedItem = null;

            var existingTitles = _explorer.Posts
                .Select(x => x.Title?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToArray();

            var topics = existingTitles.Length == 0
                ? new[] { "Getting started", "Frequently asked questions", "Complete guide", "Best practices", "Common mistakes" }
                : existingTitles.Select(TrimTopic).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();

            var baseline = BuildBaselineRows(topics);
            var aiRows = await EnrichWithAiAsync(baseline);

            foreach (var row in aiRows)
                Items.Add(row);

            PlannedCount = Items.Count;
            HighPriorityCount = Items.Count(x => x.Priority == 1);
            AiEnhancedCount = Items.Count(x => x.IsAiEnhanced);
            SelectedItem = Items.FirstOrDefault();

            StatusMessage = AiEnhancedCount > 0
                ? $"Prepared {PlannedCount} execution-ready content briefs for {_sites.SelectedSite?.Name}; {AiEnhancedCount} were refined by AI."
                : $"Prepared {PlannedCount} smart content briefs for {_sites.SelectedSite?.Name}. AI was unavailable, so the local SEO intelligence engine was used.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<ContentPlanRow> BuildBaselineRows(IReadOnlyList<string> topics)
    {
        var rows = new List<ContentPlanRow>(topics.Count);
        var targetDate = DateTime.Today.AddDays(2);

        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            var priority = i < 5 ? 1 : i < 12 ? 2 : 3;
            var intent = DetectIntent(topic);
            var keyword = BuildPrimaryKeyword(topic);
            var type = intent is "Commercial" or "Transactional" ? "Supporting article" : i % 3 == 0 ? "Pillar article" : "Supporting article";
            var title = BuildSeoTitle(topic, intent);
            var outline = BuildOutline(topic, intent);
            var requirements = BuildSeoRequirements(keyword, intent);
            var meta = BuildMetaDescription(topic, keyword, intent);

            rows.Add(new ContentPlanRow(
                priority,
                topic,
                type,
                title,
                keyword,
                intent,
                requirements,
                outline,
                meta,
                "Ready for preview",
                priority == 1 ? "High-value content gap with immediate SEO opportunity" : "Strengthen topical authority and internal-link coverage",
                targetDate.AddDays(i * 3),
                false));
        }

        return rows;
    }

    private async Task<IReadOnlyList<ContentPlanRow>> EnrichWithAiAsync(IReadOnlyList<ContentPlanRow> baseline)
    {
        try
        {
            if (!await _aiSuggestions.IsConfiguredAsync())
                return baseline;

            var inputs = baseline.Select(row => new AiSuggestionInput(
                "Content Planner",
                "ContentBrief",
                row.Topic,
                "OptimizeKeywordBrief",
                row.SuggestedTitle,
                row.SuggestedTitle,
                $"Primary keyword: {row.PrimaryKeyword}; intent: {row.SearchIntent}; requirements: {row.SeoRequirements}",
                "Low")).ToArray();

            var outputs = await _aiSuggestions.ImproveSuggestionsAsync(inputs);
            if (outputs.Count == 0)
                return baseline;

            var byTopic = outputs
                .GroupBy(x => x.ObjectId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            return baseline.Select(row =>
            {
                if (!byTopic.TryGetValue(row.Topic, out var ai))
                    return row;

                var refinedTitle = string.IsNullOrWhiteSpace(ai.ProposedValue) ? row.SuggestedTitle : ai.ProposedValue.Trim();
                var refinedReason = string.IsNullOrWhiteSpace(ai.Reason) ? row.Reason : ai.Reason.Trim();
                return row with
                {
                    SuggestedTitle = refinedTitle,
                    Reason = refinedReason,
                    Status = "AI-ready",
                    IsAiEnhanced = true
                };
            }).ToArray();
        }
        catch
        {
            return baseline;
        }
    }

    private void PrepareExecutionPreview()
    {
        if (SelectedItem is null)
            return;

        UpdatePreview(SelectedItem);
        ExecutionPreviewStatus = "Execution package prepared locally: title, keyword intent, meta description, outline, and SEO acceptance criteria are ready for Article Generator.";
    }

    private void UpdatePreview(ContentPlanRow? row)
    {
        if (row is null)
        {
            PreviewTitle = "Select a plan item";
            PreviewKeyword = "The primary keyword will appear here.";
            PreviewIntent = "Search intent will appear here.";
            PreviewMeta = "The proposed meta description will appear here.";
            PreviewOutline = "The exact article structure will appear here.";
            PreviewRequirements = "SEO requirements will appear here.";
            ExecutionPreviewStatus = "Preview only — nothing has been written to WordPress.";
            return;
        }

        PreviewTitle = row.SuggestedTitle;
        PreviewKeyword = row.PrimaryKeyword;
        PreviewIntent = row.SearchIntent;
        PreviewMeta = row.MetaDescription;
        PreviewOutline = row.Outline;
        PreviewRequirements = row.SeoRequirements;
        ExecutionPreviewStatus = row.IsAiEnhanced
            ? "AI-refined execution preview — review the exact result before creating the draft."
            : "Smart SEO execution preview — AI provider was unavailable, so deterministic keyword intelligence was used.";
    }

    private void Clear()
    {
        Items.Clear();
        SelectedItem = null;
        PlannedCount = 0;
        HighPriorityCount = 0;
        AiEnhancedCount = 0;
        StatusMessage = "Plan cleared. No WordPress data was changed.";
    }

    private static string TrimTopic(string value) => value.Length > 72 ? value[..72].Trim() : value.Trim();

    private static string DetectIntent(string topic)
    {
        var value = topic.ToLowerInvariant();
        if (ContainsAny(value, "buy", "price", "cost", "deal", "service", "booking", "order")) return "Transactional";
        if (ContainsAny(value, "best", "top", "review", "compare", "vs", "alternative")) return "Commercial";
        if (ContainsAny(value, "how", "guide", "tutorial", "what", "why", "tips", "ideas")) return "Informational";
        return "Informational";
    }

    private static string BuildPrimaryKeyword(string topic)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "of", "in", "to", "for", "with", "on", "at", "from", "is", "are"
        };
        var words = topic.Split([' ', ':', '-', '—', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim('?', '!', ',', '.', '\'', '"'))
            .Where(x => x.Length > 2 && !stopWords.Contains(x))
            .Take(6);
        return string.Join(' ', words).ToLowerInvariant();
    }

    private static string BuildSeoTitle(string topic, string intent) => intent switch
    {
        "Commercial" => $"Best {topic}: Expert Comparison and Buying Guide",
        "Transactional" => $"{topic}: Options, Pricing, and How to Choose",
        _ => $"{topic}: Complete Practical Guide"
    };

    private static string BuildMetaDescription(string topic, string keyword, string intent)
    {
        var action = intent is "Commercial" or "Transactional" ? "Compare the best options and choose confidently" : "Learn the essential steps, examples, and expert recommendations";
        var value = $"{action} in this complete guide to {keyword}. Get practical answers about {topic} and avoid common mistakes.";
        return value.Length <= 158 ? value : value[..155].TrimEnd() + "...";
    }

    private static string BuildOutline(string topic, string intent)
    {
        var sections = intent switch
        {
            "Commercial" => new[] { "Search intent and decision criteria", "Top options compared", "Pros and cons", "Who each option is for", "Final recommendation", "Frequently asked questions" },
            "Transactional" => new[] { "What to know before choosing", "Available options and pricing factors", "Step-by-step selection process", "Trust and safety checks", "Next action", "Frequently asked questions" },
            _ => new[] { $"What is {topic}?", "Why it matters", "Step-by-step process", "Practical examples", "Common mistakes", "Frequently asked questions", "Conclusion and next steps" }
        };
        return string.Join(Environment.NewLine, sections.Select((section, index) => $"H2 {index + 1}: {section}"));
    }

    private static string BuildSeoRequirements(string keyword, string intent)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"• Use primary keyword naturally in title, first paragraph, one H2, meta description, and URL slug: {keyword}");
        builder.AppendLine($"• Match {intent.ToLowerInvariant()} search intent before adding promotional language");
        builder.AppendLine("• Include 3–6 related entities and semantic keyword variations");
        builder.AppendLine("• Add at least 2 relevant internal links and one authoritative external reference");
        builder.AppendLine("• Use descriptive image ALT text and concise paragraphs");
        builder.Append("• Validate title length, meta length, heading hierarchy, and content completeness before execution");
        return builder.ToString();
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
