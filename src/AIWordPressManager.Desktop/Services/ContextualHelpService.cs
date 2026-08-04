using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace AIWordPressManager.Desktop.Services;

/// <summary>
/// Produces consistent, contextual instructions for interactive controls without
/// requiring every screen to duplicate large tooltip strings in XAML.
/// </summary>
public static class ContextualHelpService
{
    private static readonly IReadOnlyDictionary<string, string> ScreenHelp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard"] = "Review live site activity, AI-ready actions, approval work, system health, and shortcuts to the underlying records. Click any metric card to open its detailed data.",
            ["Sites"] = "Add or edit WordPress sites, test credentials, configure synchronization, automation permissions, staging, and per-site safety settings.",
            ["WordPress Explorer"] = "Browse synchronized posts, pages, media, categories, and other WordPress objects. Use filters, paging, and the row right-click menu for available actions.",
            ["Content Audit"] = "Inspect content quality findings loaded from the selected site's local snapshot and generate concrete AI actions from the detected issues.",
            ["SEO Audit"] = "Run or review SEO checks for the active site. Findings become executable or approval-routed actions rather than a single site score.",
            ["SEO History"] = "Review historical SEO audit snapshots for the active site and compare issue counts and trends over time.",
            ["Category Planner"] = "Build and review an AI-assisted taxonomy plan. Select an item to inspect its proposed category structure before execution.",
            ["Content Planner"] = "Generate an AI-enhanced content calendar with keywords, intent, titles, metadata, structure, and an exact execution preview.",
            ["Article Generator"] = "Create a structured article draft from a content-plan item. Review the generated HTML before routing it to approval or execution.",
            ["Internal Links"] = "Analyze and prepare internal-link changes. Review source, destination, anchor text, risk, and route before applying.",
            ["Post SEO Editor"] = "Select a synchronized post or page to load its live WordPress fields, edit supported values, and save through the verified REST pipeline.",
            ["Theme Inspector"] = "Inspect theme information and prepare theme-related actions. High-risk or unsupported changes remain staged or manual.",
            ["Visual Inspector"] = "Capture desktop, tablet, and mobile evidence, inspect visual signals, and compare before/after screenshots.",
            ["Design Audit"] = "Review design consistency findings and route each result to the appropriate visual, CSS, theme, or manual workflow.",
            ["Responsive Audit"] = "Review responsive layout issues across viewport sizes and prepare safe, verifiable remediation actions.",
            ["Performance"] = "Monitor application memory, CPU, database size, cooling state, and performance controls.",
            ["Accessibility"] = "Review accessibility findings and prepare actions for text, labels, focus, targets, images, and semantic structure.",
            ["Broken Links"] = "Review broken-link scans, open affected URLs, and prepare verified repair actions.",
            ["Action Center"] = "Review AI actions grouped by operational purpose and move validated actions into approval and execution workflows.",
            ["AI Studio"] = "Send structured requests to configured AI providers. Results remain local until routed into Suggested Changes.",
            ["AI Site Brain"] = "Review the active site's local AI context, facts, and reusable knowledge used by analysis and generation workflows.",
            ["Suggested Changes"] = "Review exact AI actions. Select a row to see the current value, proposed value, risk, executor, and the available apply, approve, reject, or route actions.",
            ["Approval Queue"] = "Approve or reject prepared changes. Approval alone does not write to WordPress; execution occurs through Execution Center.",
            ["Execution Center"] = "Build a safe execution plan, create backups, send supported actions to WordPress, verify saved values, capture evidence, log responses, and allow rollback.",
            ["Deletion Center"] = "Review deletion candidates and recovery options. Permanent deletion remains protected by explicit confirmation and policy.",
            ["Jobs"] = "Monitor queued, running, paused, completed, failed, and cancelled background jobs. Retry, cancel, pause, or resume when available.",
            ["Notification Center"] = "Review job and system notifications for the active site and navigate to the related workflow.",
            ["Activity Timeline"] = "Review a chronological record of audits, AI actions, synchronization, execution, and system activity.",
            ["Backups"] = "Create, download, upload, verify, and restore SQLite database backups. Restore should be performed only after confirming the selected file.",
            ["Reports"] = "Generate and export operational, SEO, content, execution, and site reports from local data.",
            ["Logs"] = "Review WordPress REST requests and responses, application log files, HTTP status, duration, correlation IDs, and AI error interpretation.",
            ["Settings"] = "Configure synchronization, AI providers, automation policy, jobs, performance, appearance, safety, screenshots, verification, and splash duration.",
            ["Help"] = "Open the user guide, system status document, keyboard shortcuts, and contextual help instructions."
        };

    public static bool IsInteractive(FrameworkElement element) => element switch
    {
        CheckBox => true,
        RadioButton => true,
        ButtonBase => true,
        MenuItem => true,
        TextBoxBase => true,
        PasswordBox => true,
        ComboBox => true,
        Slider => true,
        DataGrid => true,
        TabItem => true,
        TreeView => true,
        ListView => true,
        ListBox => true,
        _ => false
    };

    public static string GetTitle(FrameworkElement element, string currentPage)
    {
        var label = GetLabel(element);
        if (!string.IsNullOrWhiteSpace(label))
            return label;

        return string.IsNullOrWhiteSpace(currentPage) ? "Context help" : currentPage;
    }

    public static string GetHelpText(FrameworkElement element, string currentPage)
    {
        var label = GetLabel(element);
        var normalized = label.ToLowerInvariant();

        if (element is DataGrid)
            return "Review the records in this grid. Click a row to select it, use the filter and pagination controls, and right-click a selected row to see every action currently available for that record.";

        if (element is TextBoxBase or PasswordBox)
            return BuildInputHelp(label, currentPage);

        if (element is ComboBox)
            return $"Choose the required value for {Readable(label, "this setting")}. The selection is applied to the current screen or saved when you use its Save or Apply command.";

        if (element is CheckBox or RadioButton)
            return $"Turn {Readable(label, "this option")} on or off. Review the surrounding safety and automation notes before enabling automatic or high-impact behavior.";

        if (element is TabItem)
            return $"Open the {Readable(label, "selected")} section. Each tab groups related information and actions without changing WordPress by itself.";

        if (normalized.Contains("execute") || normalized.Contains("run safe") || normalized.Contains("apply now"))
            return "Starts the supported execution pipeline for the selected action: safety checks, backup, before evidence, WordPress REST request, response logging, post-write verification, after evidence, and rollback state. Unsupported or unsafe actions remain blocked.";

        if (normalized.Contains("approve"))
            return "Approves the selected prepared action and moves it toward execution. Approval does not by itself modify WordPress; the action must still pass the execution plan and safety checks.";

        if (normalized.Contains("reject"))
            return "Rejects the selected action so it will not be executed. The decision remains available in local history and audit records.";

        if (normalized.Contains("rollback") || normalized.Contains("restore"))
            return "Restores the selected operation or backup to its previous verified state. Confirm the target site and record before continuing.";

        if (normalized.Contains("refresh") || normalized.Contains("reload"))
            return "Reloads the current screen from the local SQLite snapshot and, when explicitly supported, refreshes related live status. It does not modify WordPress content.";

        if (normalized.Contains("generate") || normalized.Contains("ai run"))
            return "Runs the configured analysis or AI generation workflow for the active site. Results are stored locally as exact actions or drafts and follow the configured approval and automation policy.";

        if (normalized.Contains("backup"))
            return "Creates or opens a safety backup workflow. Backups protect local data and are required before supported WordPress execution when configured.";

        if (normalized.Contains("delete") || normalized.Contains("clear"))
            return "Removes the selected local item, filter, or queued state according to this screen. Permanent WordPress deletion requires a protected deletion workflow and explicit confirmation.";

        if (normalized.Contains("copy") || normalized.Contains("export"))
            return "Copies or exports the currently selected or visible information. This is a read-only action and does not change WordPress.";

        if (normalized.Contains("settings") || normalized.Contains("colors") || normalized.Contains("theme"))
            return "Opens or applies application configuration. Appearance changes affect the desktop application only; site automation and WordPress permissions are controlled separately per site.";

        if (normalized.Contains("help") || normalized == "?")
            return "Turns contextual help mode on or off. While help mode is active, hover over any control for instructions and click a control to open its full explanation without running the action.";

        if (element is ButtonBase or MenuItem)
            return $"Use this command to {DescribeAction(label)}. The action follows the active site's permissions, safety policy, and available workflow.";

        if (ScreenHelp.TryGetValue(currentPage, out var screenHelp))
            return screenHelp;

        return "This control belongs to the current workflow. Hover to review its purpose, or enable Help Mode and click it to open the full contextual instructions without executing the command.";
    }

    public static string GetScreenHelp(string currentPage) =>
        ScreenHelp.TryGetValue(currentPage ?? string.Empty, out var help)
            ? help
            : "Use the controls on this screen to review local data and run only the actions permitted by the active site's safety and automation settings.";

    private static string BuildInputHelp(string label, string currentPage)
    {
        var purpose = Readable(label, "this field");
        var extra = currentPage.Equals("Sites", StringComparison.OrdinalIgnoreCase)
            ? " Site credentials remain local and protected; use Test connection before saving."
            : string.Empty;
        return $"Enter or edit {purpose}. The value is not sent to WordPress until you use an explicit save, test, generate, approve, or execute action.{extra}";
    }

    private static string GetLabel(FrameworkElement element)
    {
        string? value = element switch
        {
            Button button => button.Content?.ToString(),
            MenuItem item => item.Header?.ToString(),
            CheckBox checkBox => checkBox.Content?.ToString(),
            RadioButton radioButton => radioButton.Content?.ToString(),
            TabItem tab => tab.Header?.ToString(),
            GroupBox group => group.Header?.ToString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(value) && element.ToolTip is string toolTip)
            value = toolTip;

        if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(element.Name))
            value = SplitIdentifier(element.Name);

        return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string DescribeAction(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "run the available operation";

        var cleaned = label.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }

    private static string Readable(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
                result.Append(' ');
            result.Append(character);
        }
        return result.ToString().Replace("Box", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
    }
}
