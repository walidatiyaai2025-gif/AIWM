using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Runtime.CompilerServices;
using AIWordPressManager.Application.Changes;
using AIWordPressManager.Desktop.Behaviors;
using AIWordPressManager.Desktop.Services;
using AIWordPressManager.Desktop.ViewModels;

namespace AIWordPressManager.Desktop;

public partial class MainWindow : Window
{
    private readonly ILocalizationService _localization;
    // Weak tables prevent translated controls from being retained after a screen is unloaded.
    private readonly ConditionalWeakTable<DependencyObject, OriginalText> _originalTexts = new();
    private readonly ConditionalWeakTable<DataGridColumn, OriginalText> _columnOriginals = new();
    private readonly ConditionalWeakTable<FrameworkElement, OriginalText> _toolTipOriginals = new();
    private readonly ConditionalWeakTable<Run, OriginalText> _runOriginals = new();
    private bool _localizationQueued;
    private bool _helpModeEnabled;
    private FrameworkElement? _lastHelpElement;
    private readonly ConditionalWeakTable<FrameworkElement, HelpMarker> _generatedHelp = new();

    private static readonly Dictionary<string, string[]> MenuSearchAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = ["dashboard", "home", "لوحة التحكم", "الرئيسية"],
        ["Sites"] = ["sites", "websites", "المواقع", "مواقع"],
        ["WordPress Explorer"] = ["wordpress explorer", "explorer", "مستكشف ووردبريس", "المحتوى"],
        ["Content Audit"] = ["content audit", "content", "فحص المحتوى", "تدقيق المحتوى"],
        ["SEO Audit"] = ["seo audit", "seo", "سيو", "فحص السيو", "تحسين محركات البحث"],
        ["SEO History"] = ["seo history", "history", "trend", "سجل السيو", "تاريخ السيو", "هيستوري"],
        ["Category Planner"] = ["category", "categories", "تصنيفات", "مخطط التصنيفات"],
        ["Content Planner"] = ["content planner", "مخطط المحتوى", "خطة المحتوى"],
        ["Article Generator"] = ["article", "generator", "مقالات", "إنشاء مقال"],
        ["Internal Links"] = ["internal links", "links", "روابط داخلية", "الروابط الداخلية"],
        ["Post SEO Editor"] = ["post seo", "editor", "محرر سيو", "تحرير المقال"],
        ["Theme Inspector"] = ["theme", "قالب", "فحص القالب"],
        ["Visual Inspector"] = ["visual", "تصميم", "فحص بصري"],
        ["Design Audit"] = ["design audit", "فحص التصميم"],
        ["Responsive Audit"] = ["responsive", "mobile", "تجاوب", "موبايل"],
        ["Performance"] = ["performance", "speed", "أداء", "سرعة"],
        ["Accessibility"] = ["accessibility", "إتاحة", "سهولة الوصول"],
        ["Broken Links"] = ["broken links", "روابط مكسورة"],
        ["Action Center"] = ["action center", "مركز الإجراءات"],
        ["AI Studio"] = ["ai studio", "ذكاء اصطناعي", "استوديو"],
        ["AI Site Brain"] = ["site brain", "ذاكرة الموقع", "عقل الموقع"],
        ["Suggested Changes"] = ["suggested changes", "اقتراحات", "التغييرات المقترحة"],
        ["Approval Queue"] = ["approval", "اعتماد", "قائمة الاعتماد"],
        ["Execution Center"] = ["execution", "execute", "تنفيذ", "مركز التنفيذ"],
        ["Deletion Center"] = ["delete", "deletion", "حذف", "مركز الحذف"],
        ["Jobs"] = ["jobs", "tasks", "مهام", "الوظائف"],
        ["Backups"] = ["backup", "restore", "database", "نسخ", "استعادة", "قاعدة البيانات"],
        ["Reports"] = ["reports", "تقارير"],
        ["Logs"] = ["logs", "سجلات"],
        ["Settings"] = ["settings", "إعدادات"],
        ["Help"] = ["help", "guide", "مساعدة", "دليل المستخدم"]
    };

    public MainWindow(MainWindowViewModel viewModel, ILocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            // Do not scan the complete visual tree during startup when English is active.
            // Large grids can contain thousands of generated visuals and would block the UI thread.
            FlowDirection = _localization.IsArabic
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(
                _localization.IsArabic ? "ar-KW" : "en-US");

            if (_localization.IsArabic)
            {
                QueueRuntimeLocalization();
            }
        };
        _localization.LanguageChanged += (_, _) => QueueRuntimeLocalization();

        PreviewMouseMove += MainWindow_OnPreviewMouseMoveForHelp;
        PreviewMouseLeftButtonDown += MainWindow_OnPreviewMouseLeftButtonDownForHelp;
    }

    private void QueueRuntimeLocalization()
    {
        if (_localizationQueued)
            return;

        _localizationQueued = true;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                try
                {
                    ApplyRuntimeLocalization();
                }
                finally
                {
                    _localizationQueued = false;
                }
            }));
    }

    private void ApplyRuntimeLocalization()
    {
        FlowDirection = _localization.IsArabic
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Language = System.Windows.Markup.XmlLanguage.GetLanguage(
            _localization.IsArabic ? "ar-KW" : "en-US");

        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        LocalizeVisualTree(this, visited);
    }

    private void LocalizeVisualTree(
        DependencyObject element,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
            return;

        LocalizeCurrentElement(element);

        // VisualTreeHelper only accepts Visual/Visual3D objects. TextElement/Run are
        // intentionally handled through their owning TextBlock and never passed here.
        if (element is not Visual &&
            element is not System.Windows.Media.Media3D.Visual3D)
        {
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            LocalizeVisualTree(
                VisualTreeHelper.GetChild(element, index),
                visited);
        }
    }

    private void LocalizeCurrentElement(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock textBlock:
                LocalizeTextBlock(textBlock);
                break;

            case ContentControl contentControl when contentControl.Content is string content:
                LocalizeText(contentControl, content, value => contentControl.Content = value);
                break;

            case HeaderedContentControl headered when headered.Header is string header:
                LocalizeText(headered, header, value => headered.Header = value);
                break;

            case HeaderedItemsControl headeredItems when headeredItems.Header is string itemsHeader:
                LocalizeText(headeredItems, itemsHeader, value => headeredItems.Header = value);
                break;
        }

        if (element is FrameworkElement frameworkElement &&
            frameworkElement.ToolTip is string toolTip)
        {
            var original = _toolTipOriginals.GetValue(frameworkElement, _ => new OriginalText(_localization.NormalizeEnglish(toolTip))).Value;

            frameworkElement.ToolTip = _localization.IsArabic
                ? _localization.Translate(original)
                : original;
        }

        if (element is Button navButton && navButton.Tag is string navigationKey &&
            MenuSearchAliases.ContainsKey(navigationKey))
        {
            navButton.Content = _localization.IsArabic
                ? _localization.Translate(navigationKey)
                : navigationKey;
        }

        if (element is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
            {
                if (column.Header is not string columnHeader)
                    continue;

                var original = _columnOriginals.GetValue(column, _ => new OriginalText(_localization.NormalizeEnglish(columnHeader))).Value;

                column.Header = _localization.IsArabic
                    ? _localization.Translate(original)
                    : original;
            }
        }
    }

    private void LocalizeTextBlock(TextBlock textBlock)
    {
        if (textBlock.Inlines.Count == 0)
        {
            LocalizeText(textBlock, textBlock.Text, value => textBlock.Text = value);
            return;
        }

        // Take a stable snapshot because assigning Run.Text can cause WPF to
        // rebuild the inline collection while it is being enumerated.
        var inlines = textBlock.Inlines.Cast<Inline>().ToArray();
        foreach (var inline in inlines)
        {
            LocalizeInline(inline);
        }
    }

    private void LocalizeInline(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                var original = _runOriginals.GetValue(run, _ => new OriginalText(_localization.NormalizeEnglish(run.Text))).Value;

                run.Text = _localization.IsArabic
                    ? _localization.Translate(original)
                    : original;
                break;

            case Span span:
                // Use a snapshot for nested spans for the same reason as TextBlock.Inlines.
                var children = span.Inlines.Cast<Inline>().ToArray();
                foreach (var child in children)
                    LocalizeInline(child);
                break;
        }
    }

    private void LocalizeText(
        DependencyObject owner,
        string current,
        Action<string> setter)
    {
        if (string.IsNullOrWhiteSpace(current))
            return;

        var original = _originalTexts.GetValue(owner, _ => new OriginalText(_localization.NormalizeEnglish(current))).Value;

        setter(_localization.IsArabic
            ? _localization.Translate(original)
            : original);
    }


    private void MainWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F1 &&
            System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            ToggleHelpMode();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.P &&
            (System.Windows.Input.Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) ==
            (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            OpenCommandPalette();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.K &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            SidebarSearchBox.Focus();
            SidebarSearchBox.SelectAll();
            e.Handled = true;
        }

        if (e.Key == System.Windows.Input.Key.Escape && SidebarSearchBox.IsKeyboardFocusWithin)
        {
            SidebarSearchBox.Clear();
            System.Windows.Input.Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void SidebarSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SidebarNavigationRoot is null)
            return;

        var query = NormalizeSearchText(SidebarSearchBox.Text);
        ApplySidebarFilter(SidebarNavigationRoot, query);
    }

    private static bool ApplySidebarFilter(DependencyObject parent, string query)
    {
        var hasVisibleResult = false;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ToArray())
        {
            switch (child)
            {
                case Button button when button.Tag is string destination:
                    var matches = string.IsNullOrEmpty(query) || MenuEntryMatches(destination, button.Content?.ToString(), query);
                    button.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                    hasVisibleResult |= matches;
                    break;

                case Expander expander:
                    var expanderHasResult = ApplySidebarFilter(expander, query);
                    expander.Visibility = expanderHasResult || string.IsNullOrEmpty(query)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(query) && expanderHasResult)
                        expander.IsExpanded = true;
                    hasVisibleResult |= expanderHasResult;
                    break;

                default:
                    hasVisibleResult |= ApplySidebarFilter(child, query);
                    break;
            }
        }

        return hasVisibleResult;
    }

    private static bool MenuEntryMatches(string destination, string? content, string query)
    {
        if (NormalizeSearchText(destination).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            NormalizeSearchText(content).Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return MenuSearchAliases.TryGetValue(destination, out var aliases) &&
               aliases.Any(alias => NormalizeSearchText(alias).Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim()
            .Replace("أ", "ا", StringComparison.Ordinal)
            .Replace("إ", "ا", StringComparison.Ordinal)
            .Replace("آ", "ا", StringComparison.Ordinal)
            .Replace("ى", "ي", StringComparison.Ordinal)
            .Replace("ة", "ه", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private void AiProviderApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox &&
            passwordBox.DataContext is AiProviderSettingItem item)
        {
            item.ApiKey = passwordBox.Password;
        }
    }

    private void WordPressPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Sites.Wizard.ApplicationPassword = passwordBox.Password;
    }

    private void OpenCommandPalette()
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var palette = new CommandPaletteWindow(async destination =>
        {
            if (destination == CommandPaletteWindow.OptimizeMemoryCommand)
            {
                PagedDataGridBehavior.ReleaseHiddenGridCaches();
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
                return;
            }

            if (destination == CommandPaletteWindow.CleanMemoryCommand)
            {
                if (viewModel.CleanDeviceMemoryCommand.CanExecute(null))
                    await viewModel.CleanDeviceMemoryCommand.ExecuteAsync(null);
                return;
            }

            await viewModel.NavigateCommand.ExecuteAsync(destination);
        }) { Owner = this };
        palette.ShowDialog();
    }

    private sealed class OriginalText
    {
        public OriginalText(string value) => Value = value;
        public string Value { get; }
    }
    private void SidebarNavigationRoot_OnPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var clickedButton = FindAncestor<Button>(source);
        if (clickedButton is null)
        {
            return;
        }

        var activeGroup = FindAncestor<Expander>(clickedButton);
        foreach (var group in FindVisualChildren<Expander>(SidebarNavigationRoot))
        {
            group.IsExpanded = ReferenceEquals(group, activeGroup);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is not System.Windows.Media.Visual and not System.Windows.Media.Media3D.Visual3D)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }


    private void SuggestedChangesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source) return;

        var row = ItemsControl.ContainerFromElement(grid, source) as DataGridRow;
        if (row?.Item is not SuggestedChangeItem item) return;

        row.IsSelected = true;
        grid.SelectedItem = item;
        grid.CurrentItem = item;

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.SuggestedChanges.SelectedItem = item;
    }

    private void AccentPaletteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }


    private void HelpModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleHelpMode();
    }

    private void ToggleHelpMode()
    {
        _helpModeEnabled = !_helpModeEnabled;
        HelpModeBanner.Visibility = _helpModeEnabled ? Visibility.Visible : Visibility.Collapsed;
        HelpModeButton.Content = _helpModeEnabled ? "? ON" : "?";
        HelpModeButton.ToolTip = _helpModeEnabled
            ? "Help mode is active. Click here or press F1 to return to normal operation."
            : "Context help mode (F1). Hover for instructions; click a control for full help.";
        Cursor = _helpModeEnabled ? Cursors.Help : Cursors.Arrow;
    }

    private void MainWindow_OnPreviewMouseMoveForHelp(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var element = FindHelpTarget(source);
        if (element is null || ReferenceEquals(element, _lastHelpElement))
            return;

        _lastHelpElement = element;
        EnsureContextHelp(element);
    }

    private void MainWindow_OnPreviewMouseLeftButtonDownForHelp(object sender, MouseButtonEventArgs e)
    {
        if (!_helpModeEnabled || e.OriginalSource is not DependencyObject source)
            return;

        var element = FindHelpTarget(source);
        if (element is null || ReferenceEquals(element, HelpModeButton))
            return;

        EnsureContextHelp(element);
        var currentPage = (DataContext as MainWindowViewModel)?.CurrentPage ?? string.Empty;
        var title = ContextualHelpService.GetTitle(element, currentPage);
        var instruction = ContextualHelpService.GetHelpText(element, currentPage);
        var window = new ContextHelpWindow(title, instruction, currentPage) { Owner = this };
        window.ShowDialog();
        e.Handled = true;
    }

    private FrameworkElement? FindHelpTarget(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null && !ReferenceEquals(current, this))
        {
            if (current is FrameworkElement frameworkElement &&
                ContextualHelpService.IsInteractive(frameworkElement))
            {
                return frameworkElement;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private void EnsureContextHelp(FrameworkElement element)
    {
        if (_generatedHelp.TryGetValue(element, out _))
            return;

        var currentPage = (DataContext as MainWindowViewModel)?.CurrentPage ?? string.Empty;
        var generatedInstruction = ContextualHelpService.GetHelpText(element, currentPage);
        if (element.ToolTip is string existingToolTip && !string.IsNullOrWhiteSpace(existingToolTip))
        {
            element.ToolTip = existingToolTip.Contains(generatedInstruction, StringComparison.Ordinal)
                ? existingToolTip
                : string.Join(Environment.NewLine, existingToolTip, string.Empty, generatedInstruction);
        }
        else if (element.ToolTip is null)
        {
            element.ToolTip = generatedInstruction;
        }

        ToolTipService.SetInitialShowDelay(element, 250);
        ToolTipService.SetBetweenShowDelay(element, 50);
        ToolTipService.SetShowDuration(element, 30000);
        ToolTipService.SetPlacement(element, System.Windows.Controls.Primitives.PlacementMode.Mouse);
        _generatedHelp.Add(element, new HelpMarker());
    }

    private sealed class HelpMarker { }

}
