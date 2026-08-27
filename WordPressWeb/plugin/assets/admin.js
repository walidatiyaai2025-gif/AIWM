(() => {
  'use strict';

  const root = document.getElementById('aiwm-web-root');
  const bootstrap = window.AIWM_WEB_BOOTSTRAP || {};
  if (!root) return;

  const apiFetch = window.wp && window.wp.apiFetch;
  if (apiFetch && bootstrap.nonce) {
    apiFetch.use(apiFetch.createNonceMiddleware(bootstrap.nonce));
  }

  const nav = [
    ['dashboard', 'Dashboard'],
    ['sites', 'Sites'],
    ['explorer', 'WordPress Explorer'],
    ['seo', 'SEO Audit'],
    ['suggestions', 'Suggested Changes'],
    ['approvals', 'Approval Queue'],
    ['execution', 'Execution Center'],
    ['evidence', 'Evidence Center'],
    ['settings', 'Settings / AI Providers'],
  ];

  const state = {
    route: 'dashboard',
    dashboard: null,
    health: null,
    loading: true,
    error: null,
  };

  const esc = (value) => String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');

  function metric(label, value, detail) {
    return `<article class="aiwm-card aiwm-metric"><span>${esc(label)}</span><strong>${esc(value)}</strong><small>${esc(detail)}</small></article>`;
  }

  function dashboardView() {
    if (state.loading) return '<section class="aiwm-panel"><h2>Dashboard</h2><p>Loading live WordPress state…</p></section>';
    if (state.error) return `<section class="aiwm-panel aiwm-error"><h2>Dashboard unavailable</h2><p>${esc(state.error)}</p><button class="aiwm-primary" data-action="retry">Retry</button></section>`;

    const c = state.dashboard?.counts || {};
    const health = state.health?.ok ? 'Runtime healthy' : 'Runtime requires attention';
    return `
      <section class="aiwm-page-heading">
        <div><p class="aiwm-eyebrow">OPERATIONS OVERVIEW</p><h1>Dashboard</h1><p>Live status from the WordPress Web Edition runtime.</p></div>
        <div class="aiwm-status ${state.health?.ok ? 'ok' : 'warn'}">${esc(health)}</div>
      </section>
      <section class="aiwm-metrics">
        ${metric('Managed Sites', c.sites ?? 0, `${c.verifiedSites ?? 0} verified`)}
        ${metric('Pending Changes', c.pendingRecommendations ?? 0, 'Awaiting review')}
        ${metric('Active Jobs', c.runningJobs ?? 0, 'Queued or running')}
        ${metric('Failed Executions', c.failedExecutions ?? 0, 'Needs recovery')}
      </section>
      <section class="aiwm-grid-two">
        <article class="aiwm-panel"><h2>Primary journey</h2><ol class="aiwm-journey">
          <li><b>1</b><span><strong>Connect a site</strong><small>Add and verify a WordPress target.</small></span></li>
          <li><b>2</b><span><strong>Explore & audit</strong><small>Load real WordPress content and create measurable findings.</small></span></li>
          <li><b>3</b><span><strong>Review suggestions</strong><small>Approve only explicit before/after changes.</small></span></li>
          <li><b>4</b><span><strong>Execute & verify</strong><small>Mutate safely, verify outcome, retain evidence.</small></span></li>
        </ol></article>
        <article class="aiwm-panel"><h2>Web Edition runtime</h2><dl class="aiwm-kv"><dt>Version</dt><dd>${esc(state.health?.version || bootstrap.version || 'unknown')}</dd><dt>Schema</dt><dd>${esc(state.health?.schemaVersion || 'unknown')}</dd><dt>Locale</dt><dd>${esc(bootstrap.locale || '')}</dd><dt>Direction</dt><dd>${bootstrap.isRtl ? 'RTL' : 'LTR'}</dd></dl></article>
      </section>`;
  }

  function incompleteView(title) {
    return `<section class="aiwm-page-heading"><div><p class="aiwm-eyebrow">WEB EDITION</p><h1>${esc(title)}</h1><p>This screen is under active functional implementation for Issue #11.</p></div><div class="aiwm-status warn">IN PROGRESS</div></section><section class="aiwm-panel"><h2>No fake completion</h2><p>This route is intentionally not presenting placeholder records as real data. It will be promoted only when its persisted REST/service path is implemented.</p></section>`;
  }

  function render() {
    root.dir = bootstrap.isRtl ? 'rtl' : 'ltr';
    const navHtml = nav.map(([key, label]) => `<button class="aiwm-nav-item ${state.route === key ? 'active' : ''}" data-route="${key}"><span class="aiwm-nav-dot"></span>${esc(label)}</button>`).join('');
    const label = nav.find(([key]) => key === state.route)?.[1] || 'Dashboard';
    const content = state.route === 'dashboard' ? dashboardView() : incompleteView(label);

    root.innerHTML = `<div class="aiwm-app"><aside class="aiwm-sidebar"><div class="aiwm-brand"><div class="aiwm-brand-mark">AI</div><div><strong>AI WordPress Manager</strong><small>Web Edition</small></div></div><nav>${navHtml}</nav><div class="aiwm-sidebar-foot">Demo branch<br><code>variant/wordpress-web-demo</code></div></aside><main class="aiwm-main">${content}</main></div>`;
  }

  async function loadDashboard() {
    state.loading = true;
    state.error = null;
    render();
    try {
      if (!apiFetch) throw new Error('WordPress API client is unavailable.');
      const [health, dashboard] = await Promise.all([
        apiFetch({ path: '/aiwm/v1/health' }),
        apiFetch({ path: '/aiwm/v1/dashboard' }),
      ]);
      state.health = health;
      state.dashboard = dashboard;
    } catch (error) {
      state.error = error?.message || 'Unable to load runtime state.';
    } finally {
      state.loading = false;
      render();
    }
  }

  root.addEventListener('click', (event) => {
    const route = event.target.closest('[data-route]');
    if (route) {
      state.route = route.dataset.route;
      render();
      return;
    }
    if (event.target.closest('[data-action="retry"]')) loadDashboard();
  });

  render();
  loadDashboard();
})();
