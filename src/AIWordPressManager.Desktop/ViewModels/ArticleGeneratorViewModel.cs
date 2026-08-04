using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using AIWordPressManager.Desktop.ViewModels.Sites;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIWordPressManager.Desktop.ViewModels;

public sealed partial class ArticleGeneratorViewModel : ObservableObject
{
    private readonly ContentPlannerViewModel _planner;
    private readonly SitesViewModel _sites;

    public ObservableCollection<ContentPlanRow> PlanItems => _planner.Items;
    public IAsyncRelayCommand GeneratePreviewCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IRelayCommand ClearCommand { get; }

    [ObservableProperty] private ContentPlanRow? _selectedPlanItem;
    [ObservableProperty] private string _articleTitle = string.Empty;
    [ObservableProperty] private string _focusKeyword = string.Empty;
    [ObservableProperty] private string _tone = "Professional";
    [ObservableProperty] private string _language = "English";
    [ObservableProperty] private string _metaDescription = string.Empty;
    [ObservableProperty] private string _articleHtml = string.Empty;
    [ObservableProperty] private string _statusMessage = "Select a planned topic, then generate an offline draft preview.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _wordCount;

    public ArticleGeneratorViewModel(ContentPlannerViewModel planner, SitesViewModel sites)
    {
        _planner = planner;
        _sites = sites;
        GeneratePreviewCommand = new AsyncRelayCommand(GeneratePreviewAsync, CanGenerate);
        CopyCommand = new RelayCommand(CopyDraft, () => !string.IsNullOrWhiteSpace(ArticleHtml));
        ClearCommand = new RelayCommand(Clear);
    }

    partial void OnSelectedPlanItemChanged(ContentPlanRow? value)
    {
        if (value is null) return;
        ArticleTitle = value.SuggestedTitle;
        FocusKeyword = value.Topic;
        GeneratePreviewCommand.NotifyCanExecuteChanged();
    }

    partial void OnArticleTitleChanged(string value) => GeneratePreviewCommand.NotifyCanExecuteChanged();
    partial void OnArticleHtmlChanged(string value) => CopyCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => GeneratePreviewCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        if (_planner.Items.Count == 0)
        {
            await _planner.LoadAsync();
        }

        SelectedPlanItem ??= _planner.Items.FirstOrDefault();
        StatusMessage = _sites.SelectedSite is null
            ? "Select a site first. Draft generation is site-aware and preview-only."
            : $"Ready to create a preview for {_sites.SelectedSite.Name}. Nothing will be published automatically.";
    }

    private bool CanGenerate() => !IsBusy && _sites.SelectedSite is not null && !string.IsNullOrWhiteSpace(ArticleTitle);

    private async Task GeneratePreviewAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Yield();
            var keyword = string.IsNullOrWhiteSpace(FocusKeyword) ? ArticleTitle : FocusKeyword.Trim();
            var title = ArticleTitle.Trim();
            MetaDescription = $"Discover {keyword} with a practical, structured guide covering key steps, examples, and useful recommendations.";
            if (MetaDescription.Length > 155) MetaDescription = MetaDescription[..155].TrimEnd();

            var builder = new StringBuilder();
            builder.AppendLine($"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>");
            builder.AppendLine($"<p><strong>{System.Net.WebUtility.HtmlEncode(keyword)}</strong> is the focus of this practical guide. This preview is generated locally for editorial review before any WordPress action.</p>");
            builder.AppendLine("<h2>Overview</h2>");
            builder.AppendLine($"<p>This section introduces {System.Net.WebUtility.HtmlEncode(keyword)}, explains why it matters, and sets clear expectations for the reader.</p>");
            builder.AppendLine("<h2>Key steps</h2><ol><li>Understand the goal and audience.</li><li>Review the current site content and evidence.</li><li>Apply the recommended approach safely.</li><li>Measure the result and refine it.</li></ol>");
            builder.AppendLine("<h2>Practical recommendations</h2><ul><li>Use specific examples.</li><li>Keep headings descriptive.</li><li>Add relevant internal links.</li><li>Review facts and brand voice before publishing.</li></ul>");
            builder.AppendLine("<h2>Conclusion</h2>");
            builder.AppendLine($"<p>A structured approach to {System.Net.WebUtility.HtmlEncode(keyword)} improves clarity, usefulness, and search visibility while keeping the final editorial decision with the site administrator.</p>");
            ArticleHtml = builder.ToString();
            WordCount = System.Text.RegularExpressions.Regex.Matches(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(ArticleHtml, "<[^>]+>", " ")), @"\b[\p{L}\p{N}'-]+\b").Count;
            StatusMessage = $"Preview generated locally: {WordCount} words. Review and edit it before creating a WordPress draft.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CopyDraft()
    {
        try
        {
            Clipboard.SetText(ArticleHtml);
            StatusMessage = "Draft HTML copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }

    private void Clear()
    {
        ArticleTitle = string.Empty;
        FocusKeyword = string.Empty;
        MetaDescription = string.Empty;
        ArticleHtml = string.Empty;
        WordCount = 0;
        SelectedPlanItem = null;
        StatusMessage = "Draft cleared. No WordPress data was changed.";
    }
}
