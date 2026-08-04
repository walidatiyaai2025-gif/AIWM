using System.Globalization;
using System.Windows;

namespace AIWordPressManager.Desktop.Services;

public interface ILocalizationService
{
    bool IsArabic { get; }
    event EventHandler? LanguageChanged;
    string Translate(string text);
    string NormalizeEnglish(string text);
    void ApplyEnglish();
    void ApplyArabic();
}

public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = "لوحة التحكم",
        ["SITE & DATA"] = "الموقع والبيانات",
        ["Sites"] = "المواقع",
        ["WordPress Explorer"] = "مستكشف ووردبريس",
        ["CONTENT & SEO"] = "المحتوى وتحسين محركات البحث",
        ["Content Audit"] = "فحص المحتوى",
        ["SEO Audit"] = "فحص تحسين محركات البحث",
        ["Category Planner"] = "مخطط التصنيفات",
        ["Content Planner"] = "مخطط المحتوى",
        ["Article Generator"] = "مولد المقالات",
        ["Internal Links"] = "الروابط الداخلية",
        ["Post SEO Editor"] = "محرر SEO للمقال",
        ["DESIGN & QUALITY"] = "التصميم والجودة",
        ["Theme Inspector"] = "فاحص القالب",
        ["Visual Inspector"] = "الفاحص المرئي",
        ["Design Audit"] = "فحص التصميم",
        ["Responsive Audit"] = "فحص التجاوب",
        ["Performance"] = "الأداء",
        ["Accessibility"] = "إمكانية الوصول",
        ["Broken Links"] = "الروابط المعطلة",
        ["AI ACTIONS"] = "إجراءات الذكاء الاصطناعي",
        ["Action Center"] = "مركز الإجراءات",
        ["AI Studio"] = "استوديو الذكاء الاصطناعي",
        ["AI Site Brain"] = "ذاكرة الموقع الذكية",
        ["Suggested Changes"] = "التعديلات المقترحة",
        ["Approval Queue"] = "قائمة الاعتماد",
        ["Execution Center"] = "مركز التنفيذ",
        ["Deletion Center"] = "مركز الحذف",
        ["SYSTEM"] = "النظام",
        ["Jobs"] = "المهام",
        ["Backups"] = "النسخ الاحتياطية",

        ["Database Backup & Restore"] = "نسخ واستعادة قاعدة البيانات",
        ["Import / upload backup"] = "رفع / استيراد نسخة احتياطية",
        ["Download / save copy"] = "تنزيل / حفظ نسخة",
        ["ACTIVE SITE:"] = "الموقع النشط:",
        ["Reports"] = "التقارير",
        ["Logs"] = "السجلات",
        ["Settings"] = "الإعدادات",
        ["Help & User Guide"] = "المساعدة ودليل المستخدم",
        ["Open user guide (F1)"] = "فتح دليل المستخدم (F1)",
        ["Open documentation folder"] = "فتح مجلد التوثيق",
        ["Open Word user guide   F1"] = "فتح دليل Word   F1",
        ["Keyboard shortcuts"] = "اختصارات لوحة المفاتيح",
        ["Shortcut"] = "الاختصار",
        ["Action"] = "الوظيفة",
        ["Description"] = "الوصف",
        ["GUIDE VERSION"] = "إصدار الدليل",
        ["SHORTCUTS"] = "الاختصارات",
        ["Open the complete Word guide and review the keyboard shortcuts for the most important actions."] = "افتح دليل Word الكامل وراجع اختصارات أهم وظائف التطبيق.",
        ["Use F1 at any time to open the bundled Word guide."] = "استخدم F1 في أي وقت لفتح دليل Word المرفق.",
        ["Save"] = "حفظ",
        ["Apply"] = "تطبيق",
        ["Apply now"] = "تطبيق الآن",
        ["Cancel"] = "إلغاء",
        ["Delete"] = "حذف",
        ["Approve"] = "اعتماد",
        ["Approve selected"] = "اعتماد المحدد",
        ["Approve all low risk"] = "اعتماد كل منخفض الخطورة",
        ["Reject"] = "رفض",
        ["Execute"] = "تنفيذ",
        ["Execute selected"] = "تنفيذ المحدد",
        ["Execute all ready"] = "تنفيذ كل الجاهز",
        ["Complete + execute selected"] = "استكمال وتنفيذ المحدد",
        ["Rollback"] = "استرجاع",
        ["Rollback selected"] = "استرجاع المحدد",
        ["Export"] = "تصدير",
        ["Export report"] = "تصدير التقرير",
        ["Import"] = "استيراد",
        ["Refresh"] = "تحديث",
        ["Reload"] = "إعادة تحميل",
        ["Reload approved queue"] = "إعادة تحميل قائمة المعتمد",
        ["Generate"] = "إنشاء",
        ["Search"] = "بحث",
        ["Filter"] = "تصفية",
        ["Clear"] = "مسح",
        ["Clear filter"] = "مسح التصفية",
        ["Select all"] = "تحديد الكل",
        ["Select all ready"] = "تحديد كل الجاهز",
        ["Clear selection"] = "إلغاء التحديد",
        ["Build execution plan"] = "إنشاء خطة التنفيذ",
        ["Run safe plan"] = "تشغيل الخطة الآمنة",
        ["Prepare selected"] = "تجهيز المحدد",
        ["Prepare all supported"] = "تجهيز كل المدعوم",
        ["Go to first executable"] = "الانتقال لأول عنصر قابل للتنفيذ",
        ["Retry failed"] = "إعادة محاولة الفاشل",
        ["Cancel current"] = "إلغاء العملية الحالية",
        ["Ready"] = "جاهز",
        ["Pending"] = "قيد الانتظار",
        ["Pending approval"] = "بانتظار الاعتماد",
        ["Executed"] = "تم التنفيذ",
        ["Failed"] = "فشل",
        ["Blocked / manual"] = "محظور / يدوي",
        ["Needs value"] = "يحتاج قيمة",
        ["Selected"] = "المحدد",
        ["Status"] = "الحالة",
        ["Approval"] = "الاعتماد",
        ["Risk"] = "الخطورة",
        ["Change"] = "التغيير",
        ["Object"] = "العنصر",
        ["Current value"] = "القيمة الحالية",
        ["Proposed value"] = "القيمة المقترحة",
        ["Execution preview"] = "معاينة التنفيذ",
        ["Backup required"] = "النسخ الاحتياطي مطلوب",
        ["Staging required"] = "بيئة تجريبية مطلوبة",
        ["Low"] = "منخفض",
        ["Medium"] = "متوسط",
        ["High"] = "مرتفع",
        ["NotStarted"] = "لم يبدأ",
        ["Running"] = "قيد التنفيذ",
        ["Completed"] = "مكتمل",
        ["Cancelled"] = "ملغي",
        ["Loading..."] = "جارٍ التحميل...",
        ["No site selected"] = "لم يتم اختيار موقع",
        ["Nothing executable"] = "لا توجد عمليات قابلة للتنفيذ",
        ["No safe executable actions"] = "لا توجد إجراءات آمنة قابلة للتنفيذ",
        ["First"] = "الأول",
        ["Previous"] = "السابق",
        ["Next"] = "التالي",
        ["Last"] = "الأخير",
        ["Rows"] = "الصفوف",
        ["Page"] = "الصفحة",
        ["of"] = "من",
        ["total"] = "الإجمالي",
        ["filtered rows"] = "صفوف بعد التصفية",
        ["Database"] = "قاعدة البيانات",
        ["Local database"] = "قاعدة البيانات المحلية",
        ["SQLite connected"] = "تم الاتصال بقاعدة SQLite",
        ["offline data loaded"] = "تم تحميل البيانات المحلية",
        ["Provider"] = "المزود",
        ["Model"] = "النموذج",
        ["Task"] = "المهمة",
        ["Request"] = "الطلب",
        ["Reasoning summary"] = "ملخص سبب الاقتراح",
        ["Exact AI proposal"] = "اقتراح الذكاء الاصطناعي الدقيق",
        ["PREVIEW ONLY"] = "للمعاينة فقط",
        ["Reload providers"] = "إعادة تحميل المزودين",
        ["Generate exact proposal"] = "إنشاء اقتراح دقيق",
        ["Open image"] = "فتح الصورة",
        ["Install browser"] = "تثبيت المتصفح",
        ["Inspect selected site"] = "فحص الموقع المحدد",
        ["Create suggestions"] = "إنشاء اقتراحات",
        ["Responsive captures"] = "لقطات التجاوب",
        ["Selected evidence"] = "الدليل المحدد",
        ["Inspection history"] = "سجل الفحوصات",
        ["VIEWPORTS"] = "أحجام العرض",
        ["VISUAL SIGNALS"] = "المؤشرات المرئية",
        ["LAST RUN"] = "آخر تشغيل",
        ["TREND"] = "الاتجاه",
        ["Never"] = "لم يتم",
        ["No previous run"] = "لا يوجد تشغيل سابق",
        ["Compared with previous run"] = "مقارنة بالتشغيل السابق",
        ["Theme Intelligence"] = "ذكاء القالب",
        ["Discover theme"] = "اكتشاف القالب",
        ["Test connection"] = "اختبار الاتصال",
        ["Use"] = "استخدام",
        ["Open Puter in Chrome"] = "فتح Puter في كروم",
        ["Sign in with Google"] = "تسجيل الدخول باستخدام Google",
        ["Disconnect"] = "قطع الاتصال",
        ["Create verified backup"] = "إنشاء نسخة احتياطية موثقة",
        ["Backups & Restore"] = "النسخ الاحتياطي والاستعادة",
        ["Restore from file"] = "استعادة من ملف",
        ["Restore selected"] = "استعادة المحدد",
        ["Verified recovery points"] = "نقاط الاستعادة الموثقة",
        ["Show selected in Explorer"] = "إظهار المحدد في المستكشف",
        ["Open folder"] = "فتح المجلد",
        ["Export HTML"] = "تصدير HTML",
    };


    private static readonly IReadOnlyDictionary<string, string> EnglishByArabic =
        Arabic.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public bool IsArabic { get; private set; }
    public event EventHandler? LanguageChanged;

    public string Translate(string text)
    {
        if (!IsArabic || string.IsNullOrWhiteSpace(text)) return text;
        var english = NormalizeEnglish(text);
        return Arabic.TryGetValue(english.Trim(), out var translated) ? translated : text;
    }

    public string NormalizeEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        return EnglishByArabic.TryGetValue(trimmed, out var english) ? english : text;
    }

    public void ApplyEnglish()
    {
        IsArabic = false;
        ApplyResources(false);
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyArabic()
    {
        IsArabic = true;
        ApplyResources(true);
        CultureInfo.CurrentUICulture = new CultureInfo("ar-KW");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyResources(bool arabic)
    {
        Set("L_AIStudio", arabic ? "استوديو الذكاء الاصطناعي" : "AI Studio");
        Set("L_AIStudioDescription", arabic ? "اختبر مزودًا مفعّلًا وأنشئ اقتراحًا دقيقًا قبل تطبيق أي تعديل على ووردبريس." : "Test an enabled provider and generate an exact proposal before applying anything to WordPress.");
        Set("L_ReloadProviders", arabic ? "↻  إعادة تحميل المزودين" : "↻  Reload providers");
        Set("L_Request", arabic ? "الطلب" : "Request");
        Set("L_Provider", arabic ? "المزود" : "Provider");
        Set("L_Model", arabic ? "النموذج" : "Model");
        Set("L_Task", arabic ? "المهمة" : "Task");
        Set("L_CurrentContext", arabic ? "القيمة الحالية أو السياق" : "Current value or context");
        Set("L_DesiredOutcome", arabic ? "النتيجة المطلوبة" : "Desired outcome");
        Set("L_GenerateProposal", arabic ? "▶  إنشاء اقتراح دقيق" : "▶  Generate exact proposal");
        Set("L_ExactProposal", arabic ? "اقتراح الذكاء الاصطناعي" : "Exact AI proposal");
        Set("L_PreviewOnly", arabic ? "للمعاينة فقط" : "PREVIEW ONLY");
        Set("L_Reasoning", arabic ? "ملخص سبب الاقتراح" : "Reasoning summary");
        Set("L_AiStudioNotice", arabic ? "لن يتم حفظ أي شيء من هذه الشاشة في ووردبريس. انقل النتيجة المعتمدة إلى شاشة التغييرات المقترحة قبل التنفيذ." : "Nothing from AI Studio is saved to WordPress. Move a validated result into Suggested Changes before execution.");
        Set("L_ProviderAware", arabic ? "معالجة الطلب حسب المزود" : "Provider-aware request handling");
        Set("L_ProviderAwareText", arabic ? "يحذف التطبيق المعاملات غير المدعومة في Puter، ويستخدم Ollama محليًا دون مفتاح، بينما تستخدم بقية المزودات بيانات الاعتماد المحفوظة مع التحويل التلقائي عند الفشل." : "Puter requests omit unsupported parameters. Ollama uses its local endpoint without a key. Other providers use their configured credentials and automatic fallback.");
        Set("L_DesignQuality", arabic ? "التصميم والجودة" : "DESIGN & QUALITY");
        Set("L_AiActions", arabic ? "إجراءات الذكاء الاصطناعي" : "AI ACTIONS");
        Set("L_System", arabic ? "النظام" : "SYSTEM");
    }

    private static void Set(string key, string value) => System.Windows.Application.Current.Resources[key] = value;
}
