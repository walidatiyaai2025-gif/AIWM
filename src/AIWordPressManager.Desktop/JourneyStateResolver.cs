namespace AIWordPressManager.Desktop.ViewModels;

public enum JourneyStageStatus
{
    Completed,
    Current,
    Blocked,
    Pending
}

public sealed record JourneyStateInput(
    bool HasSite,
    bool HasSnapshot,
    bool HasAnalysis,
    bool HasFindings,
    bool HasRecommendations,
    bool HasApproval,
    bool HasBackup,
    bool HasExecution,
    bool HasVerification,
    bool HasFailure,
    bool CanRollback,
    bool IsArabic = false);

public sealed record JourneyStageState(
    string Key,
    string Title,
    string Description,
    string Target,
    JourneyStageStatus Status);

public sealed record JourneyStateResult(
    IReadOnlyList<JourneyStageState> Stages,
    string Headline,
    string Summary,
    string ActionLabel,
    string Target,
    int ProgressPercent,
    bool IsBlocked,
    bool IsComplete);

/// <summary>
/// Pure, offline-first resolver for the canonical WordPress optimization journey.
/// It contains no network or WPF dependencies so local SQLite-backed state can be
/// rendered immediately and covered by deterministic tests.
/// </summary>
public static class JourneyStateResolver
{
    public static JourneyStateResult Resolve(JourneyStateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var ar = input.IsArabic;
        var definitions = BuildDefinitions(input, ar);
        var completedCount = definitions.Count(x => x.Completed);
        var failureIndex = input.HasFailure ? 6 : -1;
        var currentIndex = failureIndex >= 0
            ? failureIndex
            : definitions.FindIndex(x => !x.Completed);

        if (currentIndex < 0)
            currentIndex = definitions.Count - 1;

        var stages = new List<JourneyStageState>(definitions.Count);
        for (var index = 0; index < definitions.Count; index++)
        {
            var item = definitions[index];
            var status = item.Completed
                ? JourneyStageStatus.Completed
                : index == currentIndex
                    ? input.HasFailure ? JourneyStageStatus.Blocked : JourneyStageStatus.Current
                    : index < currentIndex
                        ? JourneyStageStatus.Blocked
                        : JourneyStageStatus.Pending;

            stages.Add(new JourneyStageState(item.Key, item.Title, item.Description, item.Target, status));
        }

        var current = definitions[currentIndex];
        var complete = definitions.All(x => x.Completed) && !input.HasFailure;
        var target = complete ? "Evidence Center" : ResolveTarget(input, current.Target);
        var action = BuildActionLabel(input, target, ar, complete);
        var headline = complete
            ? T(ar, "Journey complete", "اكتملت رحلة التحسين")
            : input.HasFailure
                ? T(ar, "Execution needs attention", "التنفيذ يحتاج إلى تدخل")
                : current.Title;
        var summary = complete
            ? T(ar,
                "Review measured results and start the next highest-impact improvement.",
                "راجع النتائج المقاسة وابدأ التحسين التالي الأعلى تأثيرًا.")
            : input.HasFailure
                ? T(ar,
                    input.CanRollback
                        ? "A change failed. Review evidence, then retry safely or roll back."
                        : "A change failed. Review the execution log before retrying.",
                    input.CanRollback
                        ? "فشل أحد التغييرات. راجع الأدلة ثم أعد المحاولة بأمان أو نفّذ الاسترجاع."
                        : "فشل أحد التغييرات. راجع سجل التنفيذ قبل إعادة المحاولة.")
                : current.Description;

        return new JourneyStateResult(
            stages,
            headline,
            summary,
            action,
            target,
            (int)Math.Round(completedCount * 100d / definitions.Count),
            input.HasFailure,
            complete);
    }

    private static List<Definition> BuildDefinitions(JourneyStateInput input, bool ar) =>
    [
        new("Site", T(ar, "Site", "الموقع"),
            T(ar, "Add or select the WordPress site to manage.", "أضف أو اختر موقع ووردبريس لإدارته."),
            "Sites", input.HasSite),
        new("Sync", T(ar, "Sync", "المزامنة"),
            T(ar, "Create a local SQLite snapshot before analysis.", "أنشئ نسخة محلية في SQLite قبل التحليل."),
            "WordPress Explorer", input.HasSnapshot),
        new("Analyze", T(ar, "Analyze", "التحليل"),
            T(ar, "Run the highest-priority available audit.", "شغّل التدقيق المتاح الأعلى أولوية."),
            "SEO Audit", input.HasAnalysis),
        new("Recommend", T(ar, "Recommend", "التوصيات"),
            T(ar, "Turn findings into reviewable AI and application recommendations.", "حوّل النتائج إلى توصيات قابلة للمراجعة من الذكاء الاصطناعي والتطبيق."),
            "Suggested Changes", input.HasRecommendations || (!input.HasFindings && input.HasAnalysis)),
        new("Approve", T(ar, "Approve", "الموافقة"),
            T(ar, "Approve only the changes allowed to reach execution.", "وافق فقط على التغييرات المسموح بوصولها إلى التنفيذ."),
            "Approval Queue", input.HasApproval),
        new("Backup", T(ar, "Backup", "النسخة الاحتياطية"),
            T(ar, "Create safety evidence before writing to WordPress.", "أنشئ دليل أمان قبل الكتابة إلى ووردبريس."),
            "Backup & Restore", input.HasBackup),
        new("Execute", T(ar, "Execute", "التنفيذ"),
            T(ar, "Apply approved, backed-up changes through the execution queue.", "طبّق التغييرات المعتمدة بعد النسخ الاحتياطي من خلال قائمة التنفيذ."),
            "Execution Center", input.HasExecution && !input.HasFailure),
        new("Verify", T(ar, "Verify", "التحقق"),
            T(ar, "Read WordPress again and preserve before/after evidence.", "اقرأ بيانات ووردبريس مرة أخرى واحتفظ بأدلة ما قبل وما بعد."),
            "Evidence Center", input.HasVerification),
        new("Complete", T(ar, "Rollback / Complete", "الاسترجاع / الاكتمال"),
            T(ar, "Keep rollback available or record measured completion.", "احتفظ بإمكانية الاسترجاع أو سجّل اكتمال النتائج المقاسة."),
            "Evidence Center", input.HasVerification && !input.HasFailure)
    ];

    private static string ResolveTarget(JourneyStateInput input, string defaultTarget)
    {
        if (!input.HasSite) return "Sites";
        if (!input.HasSnapshot) return "WordPress Explorer";
        if (!input.HasAnalysis) return "SEO Audit";
        if (input.HasFindings && !input.HasRecommendations) return "Suggested Changes";
        if (input.HasRecommendations && !input.HasApproval) return "Approval Queue";
        if (input.HasApproval && !input.HasBackup) return "Backup & Restore";
        if (input.HasApproval && input.HasBackup && !input.HasExecution) return "Execution Center";
        if (input.HasFailure) return input.CanRollback ? "Backup & Restore" : "Execution Center";
        if (input.HasExecution && !input.HasVerification) return "Evidence Center";
        return defaultTarget;
    }

    private static string BuildActionLabel(JourneyStateInput input, string target, bool ar, bool complete)
    {
        if (complete) return T(ar, "Review results", "مراجعة النتائج");
        if (input.HasFailure && input.CanRollback) return T(ar, "Review retry or rollback", "مراجعة الإعادة أو الاسترجاع");

        return target switch
        {
            "Sites" => T(ar, "Add or select site", "إضافة أو اختيار موقع"),
            "WordPress Explorer" => T(ar, "Synchronize now", "المزامنة الآن"),
            "SEO Audit" => T(ar, "Start priority audit", "بدء التدقيق ذي الأولوية"),
            "Suggested Changes" => T(ar, "Create recommendations", "إنشاء التوصيات"),
            "Approval Queue" => T(ar, "Review approvals", "مراجعة الموافقات"),
            "Backup & Restore" => T(ar, "Create safety backup", "إنشاء نسخة أمان"),
            "Execution Center" => T(ar, "Open execution center", "فتح مركز التنفيذ"),
            "Evidence Center" => T(ar, "Verify changes", "التحقق من التغييرات"),
            _ => T(ar, "Continue safely", "المتابعة بأمان")
        };
    }

    private static string T(bool ar, string en, string arabic) => ar ? arabic : en;

    private sealed record Definition(
        string Key,
        string Title,
        string Description,
        string Target,
        bool Completed);
}
