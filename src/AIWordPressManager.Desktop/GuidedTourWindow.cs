using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

public sealed class GuidedTourWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IReadOnlyList<TourStep> _steps = CreateSteps();
    private readonly TextBlock _counter = new() { FontSize = 12, Opacity = .72 };
    private readonly TextBlock _title = new() { FontSize = 25, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
    private readonly TextBlock _requirement = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _progress;
    private readonly Button _previous = Button("Previous", 90);
    private readonly Button _next = Button("Next", 90);
    private readonly Button _open = Button("Open this step", 130);
    private int _index;
    private bool _completed;

    public GuidedTourWindow(MainWindow owner, MainWindowViewModel viewModel)
    {
        Owner = owner;
        _viewModel = viewModel;
        _index = Math.Clamp(GuidedTourStateStore.Load().StepIndex, 0, _steps.Count - 1);
        _progress = new ProgressBar { Height = 7, Minimum = 0, Maximum = _steps.Count };

        Title = "AI WordPress Management — Guided Tour";
        Width = 520; Height = 430; MinWidth = 460; MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = true;
        Background = SystemColors.WindowBrush;

        var skip = Button("Skip tour", 90);
        _previous.Click += (_, _) => Move(-1);
        _next.Click += (_, _) => Move(1);
        _open.Click += (_, _) => OpenCurrentStep();
        skip.Click += (_, _) => Complete(true);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var element in new FrameworkElement[] { skip, Gap(), _previous, Gap(), _open, Gap(), _next }) buttons.Children.Add(element);

        var requirementCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 0, 120, 160)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 0, 120, 160)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14), Margin = new Thickness(0, 18, 0, 18), Child = _requirement
        };

        var panel = new Grid { Margin = new Thickness(26) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            panel.RowDefinitions.Add(new RowDefinition { Height = height });
        Add(panel, _counter, 0);
        _title.Margin = new Thickness(0, 10, 0, 12); Add(panel, _title, 1);
        Add(panel, _description, 2); Add(panel, requirementCard, 3);
        _progress.Margin = new Thickness(0, 0, 0, 18); Add(panel, _progress, 4);
        Add(panel, buttons, 5);
        Content = panel;

        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, _) =>
        {
            if (!_completed) GuidedTourStateStore.Save(new GuidedTourState(_index, false, false));
        };
        Render();
    }

    private static Button Button(string text, double width) => new() { Content = text, MinWidth = width, Padding = new Thickness(14, 8, 14, 8) };
    private static Border Gap() => new() { Width = 8 };
    private static void Add(Grid grid, UIElement child, int row) { Grid.SetRow(child, row); grid.Children.Add(child); }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right) { Move(1); e.Handled = true; }
        else if (e.Key == Key.Left) { Move(-1); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenCurrentStep(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void Move(int delta)
    {
        if (_index == _steps.Count - 1 && delta > 0) { Complete(false); return; }
        _index = Math.Clamp(_index + delta, 0, _steps.Count - 1);
        GuidedTourStateStore.Save(new GuidedTourState(_index, false, false));
        Render();
    }

    private void OpenCurrentStep()
    {
        var destination = _steps[_index].Destination;
        if (!string.IsNullOrWhiteSpace(destination) && _viewModel.NavigateCommand.CanExecute(destination))
            _viewModel.NavigateCommand.Execute(destination);
        Owner?.Activate();
        Topmost = false; Topmost = true;
    }

    private void Complete(bool skipped)
    {
        _completed = true;
        GuidedTourStateStore.Save(new GuidedTourState(_steps.Count - 1, true, skipped));
        Close();
    }

    private void Render()
    {
        var step = _steps[_index];
        _counter.Text = $"STEP {_index + 1} OF {_steps.Count}";
        _title.Text = step.Title;
        _description.Text = step.Description;
        _requirement.Text = $"Do this now: {step.RequiredAction}";
        _progress.Value = _index + 1;
        _previous.IsEnabled = _index > 0;
        _next.Content = _index == _steps.Count - 1 ? "Finish" : "Next";
        _open.IsEnabled = !string.IsNullOrWhiteSpace(step.Destination);
        _open.Content = _open.IsEnabled ? $"Open {step.Destination}" : "Current screen";
    }

    private static IReadOnlyList<TourStep> CreateSteps() =>
    [
        new("Welcome", "Follow the complete safe workflow from sign-in to a verified WordPress improvement plan. Progress is stored locally.", "Review the workflow and continue.", "Dashboard"),
        new("Add your WordPress site", "Create a connection using the site URL, WordPress username, and Application Password.", "Open Sites and add or select a site.", "Sites"),
        new("Test the connection", "Confirm REST API access and credentials before synchronization or execution.", "Run Test connection successfully.", "Sites"),
        new("Synchronize local data", "Load posts, pages, categories, tags, and media into the local SQLite snapshot.", "Open Explorer and press Synchronize now.", "WordPress Explorer"),
        new("Run Content Audit", "Measure content quality signals from the synchronized offline snapshot.", "Run Content Audit and inspect high-priority findings.", "Content Audit"),
        new("Run SEO Audit", "Inspect titles, descriptions, headings, images, canonical and indexability signals.", "Run SEO Audit and review the score.", "SEO Audit"),
        new("Check links", "Detect broken links and redirects, then generate internal-link opportunities.", "Run Broken Links, then open Internal Links.", "Broken Links"),
        new("Generate the improvement plan", "Convert audit findings into explainable proposals with current value, proposed value, risk, and expected result.", "Generate proposals in Suggested Changes.", "Suggested Changes"),
        new("Approve safe changes", "Approval changes workflow state only; unsupported or high-risk changes should be rejected or routed to a specialist.", "Approve low-risk proposals in Approval Queue.", "Approval Queue"),
        new("Prepare backup and evidence", "Review recovery and evidence requirements before any write reaches WordPress.", "Review Backups and Evidence Center.", "Backups"),
        new("Execute the plan", "Apply approved changes, record WordPress responses, and verify saved values.", "Execute selected approved items and wait for Verified.", "Execution Center"),
        new("Review results and automate", "Inspect reports, failures, retry and rollback options before scheduling recurring work.", "Open Reports, then Scheduler when the manual plan succeeds.", "Reports"),
        new("Tour complete", "Site → Sync → Audit → Recommend → Approve → Backup → Execute → Verify is now ready for daily use.", "Return to Dashboard and start working.", "Dashboard")
    ];

    private sealed record TourStep(string Title, string Description, string RequiredAction, string Destination);
}

public sealed record GuidedTourState(int StepIndex, bool Completed, bool Skipped);

public static class GuidedTourStateStore
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIWordPressManager", "guided-tour.json");

    public static GuidedTourState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new GuidedTourState(0, false, false);
            return JsonSerializer.Deserialize<GuidedTourState>(File.ReadAllText(FilePath)) ?? new GuidedTourState(0, false, false);
        }
        catch { return new GuidedTourState(0, false, false); }
    }

    public static void Save(GuidedTourState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void Reset() => Save(new GuidedTourState(0, false, false));
}
