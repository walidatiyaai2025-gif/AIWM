# AI WordPress Manager User Guide

Current guide version: **Part 56**  
Last updated: **2026-08-03**

The authoritative distributable guide is `AIWordPressManager_UserGuide_AR.docx`.

## Release maintenance rule

For every new application release:

1. Update the version and release date on the DOCX cover.
2. Update the affected feature sections.
3. Update the keyboard shortcuts table when commands change.
4. Add one row to the guide release history.
5. Render the DOCX and inspect every page before packaging.
6. Keep the DOCX under `Desktop/Documentation` so it is copied beside the executable.

## Current shortcuts

- F1: Open user guide
- Ctrl+1: Dashboard
- Ctrl+2: Sites
- Ctrl+3: WordPress Explorer
- Ctrl+4: SEO Audit
- Ctrl+5: Suggested Changes
- Ctrl+6: Execution Center
- Ctrl+7: Jobs
- Ctrl+8: Settings
- Ctrl+H: Help and shortcuts
- Ctrl+Shift+R: Refresh current screen
- Ctrl+Shift+L: Switch language
- Ctrl+Shift+T: Switch theme


## تحديث Part 56 - مستكشف WordPress المكتمل

تم تطوير شاشة **مستكشف WordPress** لتصبح مساحة قراءة وإدارة محلية متكاملة للبيانات المتزامنة.

### الوظائف الجديدة

- عرض المقالات والصفحات والتصنيفات والوسوم والوسائط داخل جداول تدعم Pagination والفلترة والفرز.
- البحث بالعنوان أو Slug أو رقم WordPress أو MIME Type.
- فلترة المقالات والصفحات حسب حالة النشر.
- عداد إجمالي وعدد النتائج الظاهرة لكل نوع محتوى.
- معاينة النص المحلي للمقال أو الصفحة المحددة.
- فتح المقال أو الصفحة في المتصفح.
- نسخ رابط المحتوى إلى الحافظة.
- فتح ملف الوسائط المحدد ونسخ رابطه.
- عرض سجل النشاط المحلي وآخر نتيجة مزامنة.
- الاستمرار في تحميل Snapshot من SQLite أولاً، ولا تبدأ المزامنة الحية إلا عند الضغط على **Synchronize now**.

### اختصار مهم

- `Ctrl + 3`: فتح مستكشف WordPress مباشرة.

## Part 57 — Content Audit completed
- Fixed runtime localization of TextBlock/Run collections by iterating stable snapshots.
- Completed Content Audit with search, severity/type filters, visible-result count, issue preview, open-page and copy-link actions.
- The screen remains offline-first and loads the latest SQLite audit automatically.

## Part 58 - Database backup and restore
- Backups screen supports creating verified SQLite backups.
- Restore selected verified backup or choose an external .db/.sqlite/.sqlite3 file.
- Restore workflow: integrity check -> current database safety backup -> close app -> replace DB -> remove WAL/SHM -> restart app.
- Update this section whenever the restore workflow or supported formats change.

## Part 64 — Performance & Memory Center

- Open **SYSTEM → Performance & Memory** to monitor application memory, total system memory pressure, CPU use, and SQLite size.
- Use **Clean memory now** to release hidden DataGrid caches, compact unused .NET memory, and ask Windows to reclaim unused pages owned by AI WordPress Manager.
- The safe cleanup does not close, suspend, or alter other applications.
- Automatic cooling begins when total system memory reaches 80% and normal background refresh resumes below 72%.
- The Command Palette (`Ctrl+Shift+P`) contains **Performance & Memory** and **Clean memory now**.
- Release and compile-fix notes are stored under the root `Files` folder.
