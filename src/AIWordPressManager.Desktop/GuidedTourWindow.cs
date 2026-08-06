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
    private readonly IReadOnlyList<TourStep> _steps;
    private readonly TextBlock _counter;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBlock _requirement;
    private readonly ProgressBar _progress;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _openButton;
    private int _index;

    public GuidedTourWindow(MainWindow owner, MainWindowViewModel viewModel)
    {
        Owner = owner;
        _viewModel = viewModel;
        _steps = CreateSteps();
        _index = Math.Clamp(GuidedTourStateStore.Load().StepIndex, 0, _steps.Count - 1);

        Title = "AI WordPress Management — Guided Tour";
        Width = 520;
        Height = 430;
        MinWidth = 460;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Topmost = true;
        Background = SystemColors.WindowBrush;

        _counter = new TextBlock { FontSize = 12, Opacity = .72 };
        _title = new TextBlock { FontSize = 25, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        _description = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        _requirement = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _progress = new ProgressBar { Height = 7, Minimum = 0, Maximum = _steps.Count };

        _previousButton = new Button { Content = "Previous", MinWidth = 90, Padding = new Thickness(14, 8, 14, 8) };
        _nextButton = new Button { Content = "Next", MinWidth = 90, Padding = new Thickness(14, 8, 14, 8) };
        _openButton = new Button { Content = "Open this step", MinWidth = 130, Padding = new Thickness(14, 8, 14, 8), FontWeight = FontWeights.SemiBold };
        var skipButton = new Button { Content = "Skip tour", MinWidth = 90, Padding = new Thickness(14, 8, 14, 8) };

        _previousButton.Click += (_, _) => Move(-1);
        _nextButton.Click += (_, _) => Move(1);
        _openButton.Click += (_, _) => OpenCurrentStep();
        skipButton.Click += (_, _) => Complete(skipped: true);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(skipButton);
        buttons.Children.Add(Spacer(8));
        buttons.Children.Add(_previousButton);
        buttons.Children.Add(Spacer(8));
        buttons.Children.Add(_openButton);
        buttons.Children.Add(Spacer(8));
        buttons.Children.Add(_nextButton);

        var panel = new Grid { Margin = new Thickness(26) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(_counter, 0); panel.Children.Add(_counter);
        Grid.SetRow(_title, 1); _title.Margin = new Thickness(0, 10, 0, 12); panel.Children.Add(_title);
        Grid.SetRow(_description, 2); panel.Children.Add(_description);

        var requirementCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 0, 120, 160)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 0, 120, 160)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 18, 0, 18),
            Child = _requirement
        };
        Grid.SetRow(requirementCard, 3); panel.Children.Add(requirementCard);
        Grid.SetRow(_progress, 4); _progress.Margin = new Thickness(0, 0, 0, 18); panel.Children.Add(_progress);
        Grid.SetRow(buttons, 5); panel.Children.Add(buttons);

        Content = panel;
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, _) => GuidedTourStateStore.Save(new GuidedTourState(_index, false, false));
        Render();
    }

    private static FrameworkElement Spacer(double width) => new Border { Width = width };

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right) { Move(1); e.Handled = true; }
        else if (e.Key == Key.Left) { Move(-1); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenCurrentStep(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private void Move(int delta)
    {
        if (_index == _steps.Count - 1 && delta > 0)
        {
            Complete(skipped: false);
            return;
        }

        _index = Math.Clamp(_index + delta, 0, _steps.Count - 1);
        GuidedTourStateStore.Save(new GuidedTourState(_index, false, false));
        Render();
    }

    private void OpenCurrentStep()
    {
        var step = _steps[_index];
        if (!string.IsNullOrWhiteSpace(step.Destination))
            _viewModel.NavigateCommand.Execute(step.Destination);

        Owner?.Activate();
        Topmost = false;
        Topmost = true;
    }

    private void Complete(bool skipped)
    {
        GuidedTourStateStore.Save(new GuidedTourState(_steps.Count - 1, true, skipped));
        DialogResult = true;
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
        _previousButton.IsEnabled = _index > 0;
        _nextButton.Content = _index == _steps.Count - 1 ? "Finish" : "Next";
        _openButton.IsEnabled = !string.IsNullOrWhiteSpace(step.Destination);
        _openButton.Content = string.IsNullOrWhiteSpace(step.Destination) ? "Current screen" : $"Open {step.Destination}";
    }

    private static IReadOnlyList<TourStep> CreateSteps() =>
    [
        new("Welcome", "This guided workflow takes you from the first sign-in to a verified WordPress improvement plan. Your progress is stored locally and can be resumed.", "Review the safety workflow, then continue.", "Dashboard"),
        new("Add your WordPress site", "Create the site connection using its URL, WordPress username, and Application Password. Credentials remain protected by the desktop application.", "Open Sites and add or select a site.", "Sites"),
        new("Test the connection", "Verify that the REST API and credentials work before any synchronization or execution.", "Run Test connection and confirm a successful response.", "Sites"),
        new("Synchronize local data", "The application loads posts, pages, categories, tags, and media into SQLite. Other screens read this offline snapshot first.", "Open WordPress Explorer and press Synchronize now.", "WordPress Explorer"),
        new("Run Content Audit", "Measure thin content, missing content signals, duplicated patterns, and other locally detectable issues.", "Run the content audit for the selected site.", "Content Audit"),
        new("Run SEO Audit", "Inspect titles, descriptions, headings, indexability, images, canonical signals, and other measurable SEO checks.", "Run SEO Audit and review high-priority findings.", "SEO Audit"),
        new("Check links", "Scan synchronized content for broken links, redirects, and unhealthy responses without mixing results between sites.", "Run Broken Links, then review Internal Links suggestions.", "Broken Links"),
        new("Generate the improvement plan", "Suggested Changes converts audit findings into explainable proposals containing current value, proposed value, reason, risk, and execution requirements.", "Generate proposals and inspect each change before approval.", "Suggested Changes"),
        new("Approve safe changes", "Approval changes workflow state only. High-risk or unsupported changes should be rejected or routed to a specialist editor.", "Approve selected low-risk proposals in Approval Queue.", "Approval Queue"),
        new("Create safety evidence", "Before execution, the application prepares backup and evidence requirements so a failed change can be diagnosed or rolled back.", "Review Backups and Evidence Center for the active site.", "Backups"),
        new("Execute the plan", "Execution Center applies approved changes, records WordPress request/response details, and verifies the saved result.", "Execute approved items and wait for Verified status.", "Execution Center"),
        new("Verify, report, and automate", "Review evidence, reports, failures, retries, and rollback options. Schedule recurring work only after the manual workflow succeeds.", "Open Reports, then configure Scheduler or AI Automation when ready.", "Reports"),
        new("Tour complete", "You now have a complete path: Site → Sync → Audit → Recommend → Approve → Backup → Execute → Verify. Reopen this tour later from Help or the startup option.", "Start working from the Dashboard.", "Dashboard")
    ];

    private sealed record TourStep(string Title, string Description, string RequiredAction, string Destination);
}

public sealed record GuidedTourState(int StepIndex, bool Completed, bool Skipped);

public static class GuidedTourStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIWordPressManager",
        "guided-tour.json");

    public static GuidedTourState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new GuidedTourState(0, false, false);
            return JsonSerializer.Deserialize<GuidedTourState>(File.ReadAllText(FilePath))
                   ?? new GuidedTourState(0, false, false);
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
        catch
        {
            // Tour persistence must never interrupt the main application.
        }
    }

    public static void Reset() => Save(new GuidedTourState(0, false, false));
}
