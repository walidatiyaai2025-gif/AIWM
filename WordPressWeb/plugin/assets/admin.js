(() => {
  'use strict';

  const root = document.getElementById('aiwm-web-root');
  const bootstrap = window.AIWM_WEB_BOOTSTRAP || {};
  if (!root) return;

  const apiFetch = window.wp && window.wp.apiFetch;
  if (apiFetch && bootstrap.nonce) apiFetch.use(apiFetch.createNonceMiddleware(bootstrap.nonce));

  const isArabic = String(bootstrap.locale || '').toLowerCase().startsWith('ar');
  const copy = {
    en: {
      dashboard: 'Dashboard', sites: 'Sites', explorer: 'WordPress Explorer', seo: 'SEO Audit', suggestions: 'Suggested Changes', approvals: 'Approval Queue', execution: 'Execution Center', evidence: 'Evidence Center', settings: 'Settings / AI Providers',
      webEdition: 'Web Edition', ops: 'OPERATIONS OVERVIEW', liveStatus: 'Live status from the WordPress Web Edition runtime.', runtimeHealthy: 'Runtime healthy', runtimeAttention: 'Runtime requires attention', managedSites: 'Managed Sites', verified: 'verified', pendingChanges: 'Pending Changes', awaitingReview: 'Awaiting review', activeJobs: 'Active Jobs', queuedRunning: 'Queued or running', failedExecutions: 'Failed Executions', needsRecovery: 'Needs recovery', primaryJourney: 'Primary journey', connectSite: 'Connect a site', connectSiteDetail: 'Add and verify a WordPress target.', exploreAudit: 'Explore & audit', exploreAuditDetail: 'Load real WordPress content and create measurable findings.', reviewSuggestions: 'Review suggestions', reviewSuggestionsDetail: 'Approve only explicit before/after changes.', executeVerify: 'Execute & verify', executeVerifyDetail: 'Mutate safely, verify outcome, retain evidence.', runtime: 'Web Edition runtime', version: 'Version', schema: 'Schema', locale: 'Locale', direction: 'Direction', loadingRuntime: 'Loading live WordPress state…', dashboardUnavailable: 'Dashboard unavailable', retry: 'Retry', inProgress: 'IN PROGRESS', noFake: 'No fake completion', noFakeBody: 'This route is intentionally not presenting placeholder records as real data. It will be promoted only when its persisted REST/service path is implemented.',
      sitesEyebrow: 'MANAGED WORDPRESS TARGETS', sitesIntro: 'Persisted sites from the AIWM runtime. Credentials remain server-side and are never echoed here.', addSite: 'Add Site', refresh: 'Refresh', loadingSites: 'Loading managed sites…', sitesUnavailable: 'Sites unavailable', noSites: 'No managed sites yet', noSitesBody: 'Add a WordPress target to begin the governed connect → audit → approve → execute journey.', name: 'Name', url: 'WordPress URL', status: 'Status', auth: 'Authentication', lastVerified: 'Last verified', never: 'Never', page: 'Page', previous: 'Previous', next: 'Next', addManagedSite: 'Add managed site', siteName: 'Site name', authType: 'Authentication type', appPassword: 'Application Password', connector: 'AIWM Connector', credential: 'Credential', credentialHint: 'Stored encrypted on the WordPress server. It is not returned to this browser after save.', credentialPlaceholder: 'Optional until verification is configured', cancel: 'Cancel', saveSite: 'Save Site', saving: 'Saving…', created: 'Site saved. Verification is still required before execution.', requiredFields: 'Site name and a valid HTTP(S) URL are required.', invalidUrl: 'Enter a valid HTTP(S) WordPress URL without embedded credentials.', demoBranch: 'Demo branch',
    },
    ar: {
      dashboard: 'لوحة التحكم', sites: 'المواقع', explorer: 'مستكشف WordPress', seo: 'تدقيق SEO', suggestions: 'التغييرات المقترحة', approvals: 'قائمة الموافقات', execution: 'مركز التنفيذ', evidence: 'مركز الأدلة', settings: 'الإعدادات / مزودو الذكاء الاصطناعي',
      webEdition: 'إصدار الويب', ops: 'نظرة تشغيلية', liveStatus: 'حالة فعلية من بيئة تشغيل AIWM Web Edition.', runtimeHealthy: 'بيئة التشغيل سليمة', runtimeAttention: 'بيئة التشغيل تحتاج مراجعة', managedSites: 'المواقع المُدارة', verified: 'تم التحقق', pendingChanges: 'تغييرات معلقة', awaitingReview: 'بانتظار المراجعة', activeJobs: 'مهام نشطة', queuedRunning: 'في الانتظار أو قيد التنفيذ', failedExecutions: 'تنفيذات فاشلة', needsRecovery: 'تحتاج معالجة', primaryJourney: 'مسار العمل الأساسي', connectSite: 'ربط موقع', connectSiteDetail: 'أضف هدف WordPress وتحقق من هويته.', exploreAudit: 'استكشاف وتدقيق', exploreAuditDetail: 'حمّل محتوى WordPress الحقيقي وأنشئ نتائج قابلة للقياس.', reviewSuggestions: 'مراجعة المقترحات', reviewSuggestionsDetail: 'اعتمد فقط تغييرات قبل/بعد واضحة.', executeVerify: 'تنفيذ وتحقق', executeVerifyDetail: 'نفّذ بأمان ثم تحقق واحتفظ بالأدلة.', runtime: 'بيئة Web Edition', version: 'الإصدار', schema: 'المخطط', locale: 'اللغة', direction: 'الاتجاه', loadingRuntime: 'جارٍ تحميل حالة WordPress الفعلية…', dashboardUnavailable: 'تعذر تحميل لوحة التحكم', retry: 'إعادة المحاولة', inProgress: 'قيد التنفيذ', noFake: 'لا توجد بيانات تجريبية مزيفة', noFakeBody: 'هذه الشاشة لا تعرض سجلات افتراضية على أنها حقيقية. سيتم تفعيلها فقط بعد اكتمال مسار التخزين والخدمات الفعلي.',
      sitesEyebrow: 'أهداف WORDPRESS المُدارة', sitesIntro: 'مواقع محفوظة فعليًا في AIWM. بيانات الاعتماد تبقى على الخادم ولا يعاد عرضها هنا.', addSite: 'إضافة موقع', refresh: 'تحديث', loadingSites: 'جارٍ تحميل المواقع…', sitesUnavailable: 'تعذر تحميل المواقع', noSites: 'لا توجد مواقع مُدارة بعد', noSitesBody: 'أضف موقع WordPress لبدء رحلة الربط ← التدقيق ← الموافقة ← التنفيذ.', name: 'الاسم', url: 'رابط WordPress', status: 'الحالة', auth: 'المصادقة', lastVerified: 'آخر تحقق', never: 'لم يتم', page: 'صفحة', previous: 'السابق', next: 'التالي', addManagedSite: 'إضافة موقع مُدار', siteName: 'اسم الموقع', authType: 'نوع المصادقة', appPassword: 'Application Password', connector: 'AIWM Connector', credential: 'بيانات الاعتماد', credentialHint: 'تُحفظ مشفرة على خادم WordPress ولا تُعاد إلى المتصفح بعد الحفظ.', credentialPlaceholder: 'اختياري إلى أن يتم إعداد التحقق', cancel: 'إلغاء', saveSite: 'حفظ الموقع', saving: 'جارٍ الحفظ…', created: 'تم حفظ الموقع. ما زال التحقق مطلوبًا قبل أي تنفيذ.', requiredFields: 'اسم الموقع ورابط HTTP(S) صحيحان مطلوبان.', invalidUrl: 'أدخل رابط WordPress صحيحًا بدون بيانات اعتماد داخل الرابط.', demoBranch: 'فرع الديمو',
    },
  };
  const t = (key) => (isArabic ? copy.ar : copy.en)[key] || copy.en[key] || key;

  const nav = [
    ['dashboard', 'dashboard'], ['sites', 'sites'], ['explorer', 'explorer'], ['seo', 'seo'], ['suggestions', 'suggestions'], ['approvals', 'approvals'], ['execution', 'execution'], ['evidence', 'evidence'], ['settings', 'settings'],
  ];

  const state = {
    route: 'dashboard', dashboard: null, health: null, loading: true, error: null,
    sites: { items: [], page: 1, perPage: 25, total: 0, totalPages: 0, loading: false, error: null },
    siteFormOpen: false, siteSaving: false, siteFormError: null, siteNotice: null,
  };

  const esc = (value) => String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');
  const siteField = (site, camel, snake) => site?.[camel] ?? site?.[snake] ?? null;
  const displayDate = (value) => {
    if (!value) return t('never');
    const parsed = new Date(String(value).replace(' ', 'T') + (String(value).includes('Z') ? '' : 'Z'));
    return Number.isNaN(parsed.getTime()) ? String(value) : new Intl.DateTimeFormat(isArabic ? 'ar' : 'en', { dateStyle: 'medium', timeStyle: 'short' }).format(parsed);
  };

  function metric(label, value, detail) {
    return `<article class="aiwm-card aiwm-metric"><span>${esc(label)}</span><strong>${esc(value)}</strong><small>${esc(detail)}</small></article>`;
  }

  function dashboardView() {
    if (state.loading) return `<section class="aiwm-panel"><h2>${esc(t('dashboard'))}</h2><p>${esc(t('loadingRuntime'))}</p></section>`;
    if (state.error) return `<section class="aiwm-panel aiwm-error"><h2>${esc(t('dashboardUnavailable'))}</h2><p>${esc(state.error)}</p><button class="aiwm-primary" data-action="retry-dashboard">${esc(t('retry'))}</button></section>`;
    const c = state.dashboard?.counts || {};
    const health = state.health?.ok ? t('runtimeHealthy') : t('runtimeAttention');
    return `<section class="aiwm-page-heading"><div><p class="aiwm-eyebrow">${esc(t('ops'))}</p><h1>${esc(t('dashboard'))}</h1><p>${esc(t('liveStatus'))}</p></div><div class="aiwm-status ${state.health?.ok ? 'ok' : 'warn'}">${esc(health)}</div></section>
      <section class="aiwm-metrics">${metric(t('managedSites'), c.sites ?? 0, `${c.verifiedSites ?? 0} ${t('verified')}`)}${metric(t('pendingChanges'), c.pendingRecommendations ?? 0, t('awaitingReview'))}${metric(t('activeJobs'), c.runningJobs ?? 0, t('queuedRunning'))}${metric(t('failedExecutions'), c.failedExecutions ?? 0, t('needsRecovery'))}</section>
      <section class="aiwm-grid-two"><article class="aiwm-panel"><h2>${esc(t('primaryJourney'))}</h2><ol class="aiwm-journey"><li><b>1</b><span><strong>${esc(t('connectSite'))}</strong><small>${esc(t('connectSiteDetail'))}</small></span></li><li><b>2</b><span><strong>${esc(t('exploreAudit'))}</strong><small>${esc(t('exploreAuditDetail'))}</small></span></li><li><b>3</b><span><strong>${esc(t('reviewSuggestions'))}</strong><small>${esc(t('reviewSuggestionsDetail'))}</small></span></li><li><b>4</b><span><strong>${esc(t('executeVerify'))}</strong><small>${esc(t('executeVerifyDetail'))}</small></span></li></ol></article><article class="aiwm-panel"><h2>${esc(t('runtime'))}</h2><dl class="aiwm-kv"><dt>${esc(t('version'))}</dt><dd>${esc(state.health?.version || bootstrap.version || 'unknown')}</dd><dt>${esc(t('schema'))}</dt><dd>${esc(state.health?.schemaVersion || 'unknown')}</dd><dt>${esc(t('locale'))}</dt><dd>${esc(bootstrap.locale || '')}</dd><dt>${esc(t('direction'))}</dt><dd>${bootstrap.isRtl ? 'RTL' : 'LTR'}</dd></dl></article></section>`;
  }

  function siteForm() {
    if (!state.siteFormOpen) return '';
    return `<section class="aiwm-panel aiwm-site-form-panel" aria-labelledby="aiwm-add-site-title"><div class="aiwm-panel-head"><div><h2 id="aiwm-add-site-title">${esc(t('addManagedSite'))}</h2><p>${esc(t('credentialHint'))}</p></div></div>${state.siteFormError ? `<div class="aiwm-inline-error" role="alert">${esc(state.siteFormError)}</div>` : ''}<form id="aiwm-site-form" class="aiwm-form"><label><span>${esc(t('siteName'))}</span><input name="name" type="text" maxlength="190" required autocomplete="off"></label><label><span>${esc(t('url'))}</span><input name="base_url" type="url" required placeholder="https://example.com" inputmode="url" autocomplete="url"></label><label><span>${esc(t('authType'))}</span><select name="auth_type"><option value="application_password">${esc(t('appPassword'))}</option><option value="connector">${esc(t('connector'))}</option></select></label><label><span>${esc(t('credential'))}</span><input name="credential" type="password" autocomplete="new-password" placeholder="${esc(t('credentialPlaceholder'))}"></label><div class="aiwm-form-actions"><button type="button" class="aiwm-secondary" data-action="cancel-site" ${state.siteSaving ? 'disabled' : ''}>${esc(t('cancel'))}</button><button type="submit" class="aiwm-primary" ${state.siteSaving ? 'disabled' : ''}>${esc(state.siteSaving ? t('saving') : t('saveSite'))}</button></div></form></section>`;
  }

  function sitesTable() {
    const s = state.sites;
    if (s.loading) return `<section class="aiwm-panel"><p>${esc(t('loadingSites'))}</p></section>`;
    if (s.error) return `<section class="aiwm-panel aiwm-error"><h2>${esc(t('sitesUnavailable'))}</h2><p>${esc(s.error)}</p><button class="aiwm-primary" data-action="retry-sites">${esc(t('retry'))}</button></section>`;
    if (!s.items.length) return `<section class="aiwm-panel aiwm-empty"><h2>${esc(t('noSites'))}</h2><p>${esc(t('noSitesBody'))}</p></section>`;
    const rows = s.items.map((site) => {
      const status = siteField(site, 'status', 'status') || 'pending';
      const statusClass = status === 'verified' ? 'ok' : status === 'failed' ? 'danger' : 'warn';
      const name = siteField(site, 'name', 'name') || `#${siteField(site, 'id', 'id') || ''}`;
      const url = siteField(site, 'baseUrl', 'base_url') || '';
      const auth = siteField(site, 'authType', 'auth_type') || '';
      const verifiedAt = siteField(site, 'lastVerifiedAt', 'last_verified_at');
      return `<tr><td><strong>${esc(name)}</strong></td><td><code class="aiwm-url">${esc(url)}</code></td><td><span class="aiwm-status ${statusClass}">${esc(status)}</span></td><td>${esc(auth)}</td><td>${esc(displayDate(verifiedAt))}</td></tr>`;
    }).join('');
    const pages = Math.max(1, s.totalPages || 1);
    return `<section class="aiwm-panel aiwm-table-panel"><div class="aiwm-table-wrap"><table class="aiwm-table"><thead><tr><th>${esc(t('name'))}</th><th>${esc(t('url'))}</th><th>${esc(t('status'))}</th><th>${esc(t('auth'))}</th><th>${esc(t('lastVerified'))}</th></tr></thead><tbody>${rows}</tbody></table></div><div class="aiwm-pagination"><span>${esc(t('page'))} ${esc(s.page)} / ${esc(pages)}</span><div><button class="aiwm-secondary" data-action="sites-prev" ${s.page <= 1 ? 'disabled' : ''}>${esc(t('previous'))}</button><button class="aiwm-secondary" data-action="sites-next" ${s.page >= pages ? 'disabled' : ''}>${esc(t('next'))}</button></div></div></section>`;
  }

  function sitesView() {
    return `<section class="aiwm-page-heading"><div><p class="aiwm-eyebrow">${esc(t('sitesEyebrow'))}</p><h1>${esc(t('sites'))}</h1><p>${esc(t('sitesIntro'))}</p></div><div class="aiwm-heading-actions"><button class="aiwm-secondary" data-action="refresh-sites" ${state.sites.loading ? 'disabled' : ''}>${esc(t('refresh'))}</button><button class="aiwm-primary" data-action="open-site">${esc(t('addSite'))}</button></div></section>${state.siteNotice ? `<div class="aiwm-notice" role="status">${esc(state.siteNotice)}</div>` : ''}${siteForm()}${sitesTable()}`;
  }

  function incompleteView(title) {
    return `<section class="aiwm-page-heading"><div><p class="aiwm-eyebrow">${esc(t('webEdition').toUpperCase())}</p><h1>${esc(title)}</h1><p>${esc(t('noFakeBody'))}</p></div><div class="aiwm-status warn">${esc(t('inProgress'))}</div></section><section class="aiwm-panel"><h2>${esc(t('noFake'))}</h2><p>${esc(t('noFakeBody'))}</p></section>`;
  }

  function render() {
    root.dir = bootstrap.isRtl ? 'rtl' : 'ltr';
    const navHtml = nav.map(([key, labelKey]) => `<button class="aiwm-nav-item ${state.route === key ? 'active' : ''}" data-route="${key}" aria-current="${state.route === key ? 'page' : 'false'}"><span class="aiwm-nav-dot"></span>${esc(t(labelKey))}</button>`).join('');
    const labelKey = nav.find(([key]) => key === state.route)?.[1] || 'dashboard';
    const content = state.route === 'dashboard' ? dashboardView() : state.route === 'sites' ? sitesView() : incompleteView(t(labelKey));
    root.innerHTML = `<div class="aiwm-app"><aside class="aiwm-sidebar"><div class="aiwm-brand"><div class="aiwm-brand-mark">AI</div><div><strong>AI WordPress Manager</strong><small>${esc(t('webEdition'))}</small></div></div><nav aria-label="AI WordPress Manager">${navHtml}</nav><div class="aiwm-sidebar-foot">${esc(t('demoBranch'))}<br><code>variant/wordpress-web-demo</code></div></aside><main class="aiwm-main">${content}</main></div>`;
  }

  async function loadDashboard() {
    state.loading = true; state.error = null; render();
    try {
      if (!apiFetch) throw new Error('WordPress API client is unavailable.');
      const [health, dashboard] = await Promise.all([apiFetch({ path: '/aiwm/v1/health' }), apiFetch({ path: '/aiwm/v1/dashboard' })]);
      state.health = health; state.dashboard = dashboard;
    } catch (error) { state.error = error?.message || 'Unable to load runtime state.'; }
    finally { state.loading = false; render(); }
  }

  async function loadSites(page = state.sites.page) {
    state.sites.loading = true; state.sites.error = null; state.sites.page = Math.max(1, page); render();
    try {
      if (!apiFetch) throw new Error('WordPress API client is unavailable.');
      const result = await apiFetch({ path: `/aiwm/v1/sites?page=${state.sites.page}&per_page=${state.sites.perPage}` });
      const pagination = result?.pagination || {};
      state.sites.items = Array.isArray(result?.items) ? result.items : [];
      state.sites.total = Number(result?.total ?? pagination.total ?? state.sites.items.length) || 0;
      state.sites.totalPages = Number(result?.totalPages ?? pagination.pages ?? Math.ceil(state.sites.total / state.sites.perPage)) || 0;
    } catch (error) { state.sites.error = error?.message || 'Unable to load managed sites.'; state.sites.items = []; }
    finally { state.sites.loading = false; render(); }
  }

  function validSiteUrl(raw) {
    try {
      const value = new URL(raw);
      return ['http:', 'https:'].includes(value.protocol) && !value.username && !value.password && Boolean(value.hostname);
    } catch (_) { return false; }
  }

  async function submitSite(form) {
    const data = new FormData(form);
    const name = String(data.get('name') || '').trim();
    const baseUrl = String(data.get('base_url') || '').trim();
    if (!name || !baseUrl) { state.siteFormError = t('requiredFields'); render(); return; }
    if (!validSiteUrl(baseUrl)) { state.siteFormError = t('invalidUrl'); render(); return; }
    state.siteSaving = true; state.siteFormError = null; state.siteNotice = null; render();
    try {
      const payload = { name, base_url: baseUrl, auth_type: String(data.get('auth_type') || 'application_password') };
      const credential = String(data.get('credential') || '');
      if (credential) payload.credential = credential;
      await apiFetch({ path: '/aiwm/v1/sites', method: 'POST', data: payload });
      state.siteFormOpen = false; state.siteNotice = t('created'); state.sites.page = 1;
      await loadSites(1);
      await loadDashboard();
      state.route = 'sites';
    } catch (error) { state.siteFormError = error?.message || 'Unable to save managed site.'; }
    finally { state.siteSaving = false; render(); }
  }

  root.addEventListener('click', (event) => {
    const route = event.target.closest('[data-route]');
    if (route) {
      state.route = route.dataset.route;
      state.siteNotice = null;
      render();
      if (state.route === 'sites' && !state.sites.loading && !state.sites.items.length && !state.sites.error) loadSites(1);
      return;
    }
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (!action) return;
    if (action === 'retry-dashboard') loadDashboard();
    if (action === 'retry-sites' || action === 'refresh-sites') loadSites(state.sites.page);
    if (action === 'open-site') { state.siteFormOpen = true; state.siteFormError = null; state.siteNotice = null; render(); }
    if (action === 'cancel-site') { state.siteFormOpen = false; state.siteFormError = null; render(); }
    if (action === 'sites-prev' && state.sites.page > 1) loadSites(state.sites.page - 1);
    if (action === 'sites-next' && state.sites.page < state.sites.totalPages) loadSites(state.sites.page + 1);
  });

  root.addEventListener('submit', (event) => {
    if (event.target.id !== 'aiwm-site-form') return;
    event.preventDefault();
    if (!state.siteSaving) submitSite(event.target);
  });

  render();
  loadDashboard();
})();