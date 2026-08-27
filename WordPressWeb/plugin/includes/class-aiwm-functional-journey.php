<?php

if (!defined('ABSPATH')) {
    exit;
}

final class AIWM_Web_Functional_Journey
{
    private const REST_NAMESPACE = 'aiwm/v1';
    private const CAPABILITY = 'manage_aiwm';
    private const CRON_HOOK = 'aiwm_web_run_journey_job';
    private const MAX_EXPLORER_PAGE_SIZE = 50;
    private const AUDIT_PAGE_SIZE = 10;

    public static function boot(): void
    {
        add_action('rest_api_init', [self::class, 'register_routes']);
        add_action(self::CRON_HOOK, [self::class, 'run_job'], 10, 1);
    }

    public static function register_routes(): void
    {
        register_rest_route(self::REST_NAMESPACE, '/journey/sites', [
            [
                'methods' => WP_REST_Server::READABLE,
                'callback' => [self::class, 'rest_sites'],
                'permission_callback' => [self::class, 'can_manage'],
            ],
            [
                'methods' => WP_REST_Server::CREATABLE,
                'callback' => [self::class, 'rest_add_site'],
                'permission_callback' => [self::class, 'can_manage'],
            ],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/sites/(?P<id>\d+)/verify', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_verify_site'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/sites/(?P<id>\d+)/sync', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_queue_sync'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/sites/(?P<id>\d+)/explorer', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_explorer'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/sites/(?P<id>\d+)/audits', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_queue_audit'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/audits/(?P<id>\d+)', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_audit'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/audits/(?P<id>\d+)/recommend', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_queue_recommendations'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/recommendations', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_recommendations'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/recommendations/(?P<id>\d+)/decision', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_recommendation_decision'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/recommendations/(?P<id>\d+)/execute', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_queue_execution'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/jobs/(?P<id>\d+)', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_job'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/jobs/(?P<id>\d+)/run', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_run_job'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/jobs/(?P<id>\d+)/cancel', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_cancel_job'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/jobs/(?P<id>\d+)/retry', [
            'methods' => WP_REST_Server::CREATABLE,
            'callback' => [self::class, 'rest_retry_job'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/executions/(?P<id>\d+)', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_execution'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/journey/evidence', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_evidence'],
            'permission_callback' => [self::class, 'can_manage'],
        ]);
    }

    public static function can_manage(): bool
    {
        return current_user_can(self::CAPABILITY);
    }

    public static function rest_sites(): WP_REST_Response
    {
        global $wpdb;
        $table = self::table('sites');
        $rows = $wpdb->get_results("SELECT id, name, base_url, status, auth_type, credential_ref, last_verified_at, created_at, updated_at FROM {$table} ORDER BY id DESC LIMIT 100", ARRAY_A);
        return new WP_REST_Response(['items' => array_map([self::class, 'public_site'], $rows ?: [])]);
    }

    public static function rest_add_site(WP_REST_Request $request)
    {
        global $wpdb;
        $data = self::json_params($request);
        $name = sanitize_text_field($data['name'] ?? '');
        $baseUrl = self::normalize_base_url($data['base_url'] ?? '');
        $credentialRef = sanitize_text_field($data['credential_ref'] ?? '');

        if ($name === '' || is_wp_error($baseUrl)) {
            return is_wp_error($baseUrl) ? $baseUrl : self::error('aiwm_site_name_required', __('Site name is required.', 'aiwm-web'), 400);
        }
        if (!preg_match('/^[A-Za-z0-9_.:-]{1,190}$/', $credentialRef)) {
            return self::error('aiwm_credential_ref_invalid', __('A server-side credential reference is required.', 'aiwm-web'), 400);
        }

        $now = current_time('mysql', true);
        $inserted = $wpdb->insert(self::table('sites'), [
            'name' => $name,
            'base_url' => $baseUrl,
            'status' => 'pending',
            'auth_type' => 'application_password',
            'credential_ref' => $credentialRef,
            'created_at' => $now,
            'updated_at' => $now,
        ], ['%s', '%s', '%s', '%s', '%s', '%s', '%s']);

        if (!$inserted) {
            return self::error('aiwm_site_insert_failed', __('Unable to persist the managed site.', 'aiwm-web'), 500);
        }
        $site = self::get_site((int) $wpdb->insert_id);
        return new WP_REST_Response(self::public_site($site), 201);
    }

    public static function rest_verify_site(WP_REST_Request $request)
    {
        global $wpdb;
        $site = self::get_site((int) $request['id']);
        if (!$site) {
            return self::error('aiwm_site_not_found', __('Managed site was not found.', 'aiwm-web'), 404);
        }

        $identity = self::remote_request($site, 'GET', '/wp-json/');
        if (is_wp_error($identity)) {
            self::mark_site_verification($site['id'], 'failed');
            return $identity;
        }
        $me = self::remote_request($site, 'GET', '/wp-json/wp/v2/users/me?context=edit');
        if (is_wp_error($me)) {
            self::mark_site_verification($site['id'], 'failed');
            return $me;
        }

        $expectedHost = strtolower((string) wp_parse_url($site['base_url'], PHP_URL_HOST));
        $reportedUrl = is_array($identity) ? ($identity['url'] ?? $identity['home'] ?? '') : '';
        $reportedHost = strtolower((string) wp_parse_url((string) $reportedUrl, PHP_URL_HOST));
        if ($reportedHost !== '' && $expectedHost !== $reportedHost) {
            self::mark_site_verification($site['id'], 'failed');
            return self::error('aiwm_target_identity_mismatch', __('Remote WordPress identity does not match the configured target.', 'aiwm-web'), 409);
        }

        $now = current_time('mysql', true);
        $wpdb->update(self::table('sites'), [
            'status' => 'verified',
            'last_verified_at' => $now,
            'updated_at' => $now,
        ], ['id' => (int) $site['id']], ['%s', '%s', '%s'], ['%d']);

        return new WP_REST_Response([
            'site' => self::public_site(self::get_site((int) $site['id'])),
            'target' => [
                'name' => sanitize_text_field($identity['name'] ?? ''),
                'url' => esc_url_raw($reportedUrl),
                'userId' => absint($me['id'] ?? 0),
                'userName' => sanitize_text_field($me['name'] ?? $me['username'] ?? ''),
            ],
            'verifiedAt' => gmdate('c'),
        ]);
    }

    public static function rest_queue_sync(WP_REST_Request $request)
    {
        $site = self::verified_site((int) $request['id']);
        if (is_wp_error($site)) {
            return $site;
        }
        $job = self::queue_job((int) $site['id'], 'wp_sync', [], 'sync:' . $site['id'] . ':' . wp_generate_uuid4());
        return is_wp_error($job) ? $job : new WP_REST_Response(self::public_job($job), 202);
    }

    public static function rest_explorer(WP_REST_Request $request)
    {
        $site = self::verified_site((int) $request['id']);
        if (is_wp_error($site)) {
            return $site;
        }
        $type = sanitize_key((string) ($request->get_param('type') ?: 'posts'));
        if (!in_array($type, ['posts', 'pages'], true)) {
            return self::error('aiwm_explorer_type_invalid', __('Explorer supports posts and pages in the current demo path.', 'aiwm-web'), 400);
        }
        $page = max(1, absint($request->get_param('page') ?: 1));
        $perPage = min(self::MAX_EXPLORER_PAGE_SIZE, max(1, absint($request->get_param('per_page') ?: 20)));
        $items = self::fetch_resources($site, $type, $page, $perPage);
        if (is_wp_error($items)) {
            return $items;
        }
        return new WP_REST_Response([
            'siteId' => (int) $site['id'],
            'type' => $type,
            'page' => $page,
            'perPage' => $perPage,
            'items' => array_map([self::class, 'public_resource'], $items),
        ]);
    }

    public static function rest_queue_audit(WP_REST_Request $request)
    {
        global $wpdb;
        $site = self::verified_site((int) $request['id']);
        if (is_wp_error($site)) {
            return $site;
        }
        $now = current_time('mysql', true);
        $ok = $wpdb->insert(self::table('audits'), [
            'site_id' => (int) $site['id'],
            'status' => 'queued',
            'score' => null,
            'findings_count' => 0,
            'started_at' => null,
            'completed_at' => null,
            'created_at' => $now,
        ], ['%d', '%s', '%f', '%d', '%s', '%s', '%s']);
        if (!$ok) {
            return self::error('aiwm_audit_insert_failed', __('Unable to create the persisted audit.', 'aiwm-web'), 500);
        }
        $auditId = (int) $wpdb->insert_id;
        $job = self::queue_job((int) $site['id'], 'seo_audit', ['audit_id' => $auditId], 'audit:' . $auditId);
        if (is_wp_error($job)) {
            return $job;
        }
        return new WP_REST_Response(['auditId' => $auditId, 'job' => self::public_job($job)], 202);
    }

    public static function rest_audit(WP_REST_Request $request)
    {
        global $wpdb;
        $auditId = (int) $request['id'];
        $audit = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('audits') . ' WHERE id = %d', $auditId), ARRAY_A);
        if (!$audit) {
            return self::error('aiwm_audit_not_found', __('Audit was not found.', 'aiwm-web'), 404);
        }
        $job = $wpdb->get_row($wpdb->prepare(
            'SELECT * FROM ' . self::table('jobs') . ' WHERE idempotency_key = %s LIMIT 1',
            'audit:' . $auditId
        ), ARRAY_A);
        $payload = $job ? self::decode_json($job['payload_json']) : [];
        return new WP_REST_Response([
            'audit' => self::public_audit($audit),
            'job' => $job ? self::public_job($job) : null,
            'findings' => is_array($payload['findings'] ?? null) ? $payload['findings'] : [],
            'scoreInputs' => is_array($payload['score_inputs'] ?? null) ? $payload['score_inputs'] : [],
        ]);
    }

    public static function rest_queue_recommendations(WP_REST_Request $request)
    {
        global $wpdb;
        $auditId = (int) $request['id'];
        $audit = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('audits') . ' WHERE id = %d', $auditId), ARRAY_A);
        if (!$audit) {
            return self::error('aiwm_audit_not_found', __('Audit was not found.', 'aiwm-web'), 404);
        }
        if ($audit['status'] !== 'completed') {
            return self::error('aiwm_audit_not_completed', __('Audit must complete before AI recommendations are queued.', 'aiwm-web'), 409);
        }
        $job = self::queue_job((int) $audit['site_id'], 'ai_recommend', ['audit_id' => $auditId], 'recommend:' . $auditId);
        return is_wp_error($job) ? $job : new WP_REST_Response(self::public_job($job), 202);
    }

    public static function rest_recommendations(WP_REST_Request $request): WP_REST_Response
    {
        global $wpdb;
        $siteId = absint($request->get_param('site_id') ?: 0);
        $status = sanitize_key((string) ($request->get_param('status') ?: ''));
        $where = [];
        $args = [];
        if ($siteId > 0) {
            $where[] = 'site_id = %d';
            $args[] = $siteId;
        }
        if ($status !== '') {
            $where[] = 'status = %s';
            $args[] = $status;
        }
        $sql = 'SELECT * FROM ' . self::table('recommendations');
        if ($where) {
            $sql .= ' WHERE ' . implode(' AND ', $where);
        }
        $sql .= ' ORDER BY id DESC LIMIT 100';
        if ($args) {
            $sql = $wpdb->prepare($sql, ...$args);
        }
        $rows = $wpdb->get_results($sql, ARRAY_A) ?: [];
        return new WP_REST_Response(['items' => array_map([self::class, 'public_recommendation'], $rows)]);
    }

    public static function rest_recommendation_decision(WP_REST_Request $request)
    {
        global $wpdb;
        $id = (int) $request['id'];
        $recommendation = self::get_recommendation($id);
        if (!$recommendation) {
            return self::error('aiwm_recommendation_not_found', __('Suggested change was not found.', 'aiwm-web'), 404);
        }
        $data = self::json_params($request);
        $decision = sanitize_key($data['decision'] ?? '');
        if (!in_array($decision, ['approve', 'reject'], true)) {
            return self::error('aiwm_decision_invalid', __('Decision must be approve or reject.', 'aiwm-web'), 400);
        }
        if ($recommendation['status'] !== 'pending') {
            return self::error('aiwm_decision_already_recorded', __('This suggested change already has a decision.', 'aiwm-web'), 409);
        }
        $status = $decision === 'approve' ? 'approved' : 'rejected';
        $wpdb->update(self::table('recommendations'), [
            'status' => $status,
            'updated_at' => current_time('mysql', true),
        ], ['id' => $id], ['%s', '%s'], ['%d']);
        return new WP_REST_Response(self::public_recommendation(self::get_recommendation($id)));
    }

    public static function rest_queue_execution(WP_REST_Request $request)
    {
        $recommendation = self::get_recommendation((int) $request['id']);
        if (!$recommendation) {
            return self::error('aiwm_recommendation_not_found', __('Suggested change was not found.', 'aiwm-web'), 404);
        }
        if (!in_array($recommendation['status'], ['approved', 'executed'], true)) {
            return self::error('aiwm_recommendation_not_approved', __('Only an approved suggested change can be executed.', 'aiwm-web'), 409);
        }
        $job = self::queue_job((int) $recommendation['site_id'], 'execute_recommendation', [
            'recommendation_id' => (int) $recommendation['id'],
        ], 'execute:recommendation:' . $recommendation['id']);
        return is_wp_error($job) ? $job : new WP_REST_Response(self::public_job($job), 202);
    }

    public static function rest_job(WP_REST_Request $request)
    {
        $job = self::get_job((int) $request['id']);
        return $job ? new WP_REST_Response(self::public_job($job)) : self::error('aiwm_job_not_found', __('Job was not found.', 'aiwm-web'), 404);
    }

    public static function rest_run_job(WP_REST_Request $request)
    {
        $id = (int) $request['id'];
        $job = self::get_job($id);
        if (!$job) {
            return self::error('aiwm_job_not_found', __('Job was not found.', 'aiwm-web'), 404);
        }
        if ($job['status'] === 'completed') {
            return new WP_REST_Response(self::public_job($job));
        }
        if ($job['status'] !== 'queued') {
            return self::error('aiwm_job_not_queued', __('Only a queued job can be run directly.', 'aiwm-web'), 409);
        }
        self::run_job($id);
        return new WP_REST_Response(self::public_job(self::get_job($id)));
    }

    public static function rest_cancel_job(WP_REST_Request $request)
    {
        global $wpdb;
        $id = (int) $request['id'];
        $job = self::get_job($id);
        if (!$job) {
            return self::error('aiwm_job_not_found', __('Job was not found.', 'aiwm-web'), 404);
        }
        if (in_array($job['status'], ['completed', 'cancelled'], true)) {
            return new WP_REST_Response(self::public_job($job));
        }
        if ($job['status'] === 'failed') {
            return self::error('aiwm_job_terminal', __('Failed jobs must be retried, not cancelled.', 'aiwm-web'), 409);
        }
        if ($job['status'] === 'running') {
            return self::error('aiwm_job_already_running', __('This bounded job is already running and cannot be safely cancelled mid-request.', 'aiwm-web'), 409);
        }
        $wpdb->update(self::table('jobs'), [
            'status' => 'cancelled',
            'finished_at' => current_time('mysql', true),
        ], ['id' => $id]);
        return new WP_REST_Response(self::public_job(self::get_job($id)));
    }

    public static function rest_retry_job(WP_REST_Request $request)
    {
        global $wpdb;
        $id = (int) $request['id'];
        $job = self::get_job($id);
        if (!$job) {
            return self::error('aiwm_job_not_found', __('Job was not found.', 'aiwm-web'), 404);
        }
        if ($job['status'] === 'completed') {
            return new WP_REST_Response(self::public_job($job));
        }
        if (!in_array($job['status'], ['failed', 'cancelled', 'cancel_requested'], true)) {
            return self::error('aiwm_job_not_retryable', __('Only failed or cancelled jobs can be retried.', 'aiwm-web'), 409);
        }
        $payload = self::decode_json($job['payload_json']);
        $payload['attempt'] = absint($payload['attempt'] ?? 1) + 1;
        $wpdb->update(self::table('jobs'), [
            'status' => 'queued',
            'payload_json' => wp_json_encode($payload),
            'error_json' => null,
            'started_at' => null,
            'finished_at' => null,
        ], ['id' => $id]);
        self::schedule_job($id);
        return new WP_REST_Response(self::public_job(self::get_job($id)), 202);
    }

    public static function rest_execution(WP_REST_Request $request)
    {
        global $wpdb;
        $row = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('executions') . ' WHERE id = %d', (int) $request['id']), ARRAY_A);
        return $row ? new WP_REST_Response(self::public_execution($row)) : self::error('aiwm_execution_not_found', __('Execution was not found.', 'aiwm-web'), 404);
    }

    public static function rest_evidence(WP_REST_Request $request): WP_REST_Response
    {
        global $wpdb;
        $siteId = absint($request->get_param('site_id') ?: 0);
        $executionId = absint($request->get_param('execution_id') ?: 0);
        $where = [];
        $args = [];
        if ($siteId) {
            $where[] = 'site_id = %d';
            $args[] = $siteId;
        }
        if ($executionId) {
            $where[] = 'execution_id = %d';
            $args[] = $executionId;
        }
        $sql = 'SELECT * FROM ' . self::table('evidence');
        if ($where) {
            $sql .= ' WHERE ' . implode(' AND ', $where);
        }
        $sql .= ' ORDER BY id DESC LIMIT 100';
        if ($args) {
            $sql = $wpdb->prepare($sql, ...$args);
        }
        $rows = $wpdb->get_results($sql, ARRAY_A) ?: [];
        return new WP_REST_Response(['items' => array_map([self::class, 'public_evidence'], $rows)]);
    }

    public static function run_job($jobId): void
    {
        global $wpdb;
        $jobId = absint($jobId);
        $job = self::get_job($jobId);
        if (!$job || $job['status'] !== 'queued') {
            return;
        }

        $wpdb->update(self::table('jobs'), [
            'status' => 'running',
            'started_at' => current_time('mysql', true),
            'error_json' => null,
        ], ['id' => $jobId]);

        $job = self::get_job($jobId);
        $result = null;
        if ($job['type'] === 'wp_sync') {
            $result = self::process_sync($job);
        } elseif ($job['type'] === 'seo_audit') {
            $result = self::process_audit($job);
        } elseif ($job['type'] === 'ai_recommend') {
            $result = self::process_recommendations($job);
        } elseif ($job['type'] === 'execute_recommendation') {
            $result = self::process_execution($job);
        } else {
            $result = self::error('aiwm_job_type_unsupported', __('Unsupported functional journey job type.', 'aiwm-web'), 400);
        }

        $current = self::get_job($jobId);
        if ($current && $current['status'] === 'cancel_requested') {
            $wpdb->update(self::table('jobs'), [
                'status' => 'cancelled',
                'finished_at' => current_time('mysql', true),
            ], ['id' => $jobId]);
            return;
        }

        if (is_wp_error($result)) {
            $error = [
                'code' => $result->get_error_code(),
                'message' => $result->get_error_message(),
                'data' => self::safe_error_data($result->get_error_data()),
            ];
            $wpdb->update(self::table('jobs'), [
                'status' => $result->get_error_code() === 'aiwm_job_cancelled' ? 'cancelled' : 'failed',
                'error_json' => wp_json_encode($error),
                'finished_at' => current_time('mysql', true),
            ], ['id' => $jobId]);
            return;
        }

        $payload = self::decode_json($current['payload_json'] ?? $job['payload_json']);
        if (is_array($result)) {
            $payload = array_replace_recursive($payload, $result);
        }
        $wpdb->update(self::table('jobs'), [
            'status' => 'completed',
            'progress_current' => 1,
            'progress_total' => 1,
            'payload_json' => wp_json_encode($payload),
            'finished_at' => current_time('mysql', true),
        ], ['id' => $jobId]);
    }

    private static function process_sync(array $job)
    {
        $site = self::verified_site((int) $job['site_id']);
        if (is_wp_error($site)) {
            return $site;
        }
        $snapshot = [];
        foreach (['posts', 'pages'] as $type) {
            $items = self::fetch_resources($site, $type, 1, self::AUDIT_PAGE_SIZE);
            if (is_wp_error($items)) {
                return $items;
            }
            $snapshot[$type] = array_map([self::class, 'public_resource'], $items);
        }
        return ['snapshot' => $snapshot, 'synced_at' => gmdate('c')];
    }

    private static function process_audit(array $job)
    {
        global $wpdb;
        $payload = self::decode_json($job['payload_json']);
        $auditId = absint($payload['audit_id'] ?? 0);
        if (!$auditId) {
            return self::error('aiwm_audit_missing', __('Audit job has no persisted audit identifier.', 'aiwm-web'), 500);
        }
        $site = self::verified_site((int) $job['site_id']);
        if (is_wp_error($site)) {
            return $site;
        }
        $wpdb->update(self::table('audits'), [
            'status' => 'running',
            'started_at' => current_time('mysql', true),
        ], ['id' => $auditId]);

        $findings = [];
        $checks = 0;
        $passed = 0;
        foreach (['posts', 'pages'] as $type) {
            $items = self::fetch_resources($site, $type, 1, self::AUDIT_PAGE_SIZE);
            if (is_wp_error($items)) {
                self::fail_audit($auditId);
                return $items;
            }
            foreach ($items as $item) {
                $analysis = self::analyze_resource($type, $item);
                $checks += $analysis['checks'];
                $passed += $analysis['passed'];
                array_push($findings, ...$analysis['findings']);
            }
        }

        if ($checks < 1) {
            self::fail_audit($auditId);
            return self::error('aiwm_audit_no_content', __('No posts or pages were available for the bounded SEO audit.', 'aiwm-web'), 409);
        }
        if (self::job_cancel_requested((int) $job['id'])) {
            self::fail_audit($auditId, 'cancelled');
            return self::error('aiwm_job_cancelled', __('Audit was cancelled before results were committed.', 'aiwm-web'), 409);
        }

        $score = round(($passed / $checks) * 100, 2);
        $wpdb->update(self::table('audits'), [
            'status' => 'completed',
            'score' => $score,
            'findings_count' => count($findings),
            'completed_at' => current_time('mysql', true),
        ], ['id' => $auditId]);

        return [
            'findings' => $findings,
            'score_inputs' => ['checks' => $checks, 'passed' => $passed, 'failed' => $checks - $passed],
            'score' => $score,
            'audited_at' => gmdate('c'),
        ];
    }

    private static function process_recommendations(array $job)
    {
        global $wpdb;
        $payload = self::decode_json($job['payload_json']);
        $auditId = absint($payload['audit_id'] ?? 0);
        if (!$auditId) {
            return self::error('aiwm_audit_missing', __('Recommendation job has no audit identifier.', 'aiwm-web'), 500);
        }
        $auditJob = $wpdb->get_row($wpdb->prepare(
            'SELECT * FROM ' . self::table('jobs') . ' WHERE idempotency_key = %s AND status = %s LIMIT 1',
            'audit:' . $auditId, 'completed'
        ), ARRAY_A);
        if (!$auditJob) {
            return self::error('aiwm_audit_evidence_missing', __('Persisted audit findings were not found.', 'aiwm-web'), 409);
        }
        $existingCount = (int) $wpdb->get_var($wpdb->prepare(
            'SELECT COUNT(*) FROM ' . self::table('recommendations') . ' WHERE audit_id = %d',
            $auditId
        ));
        if ($existingCount > 0) {
            return ['recommendation_count' => $existingCount, 'provider' => 'Gemini', 'reused' => true];
        }
        $auditPayload = self::decode_json($auditJob['payload_json']);
        $findings = is_array($auditPayload['findings'] ?? null) ? $auditPayload['findings'] : [];
        $candidates = [];
        foreach ($findings as $finding) {
            $field = sanitize_key($finding['field'] ?? '');
            if (!in_array($field, ['title', 'excerpt', 'slug'], true)) {
                continue;
            }
            $candidates[] = [
                'object_type' => sanitize_key($finding['object_type'] ?? ''),
                'object_id' => absint($finding['object_id'] ?? 0),
                'field' => $field,
                'current' => is_string($finding['current'] ?? null) ? $finding['current'] : '',
                'finding' => sanitize_text_field($finding['message'] ?? ''),
            ];
        }
        $provider = apply_filters('aiwm_web_ai_provider', null, 'gemini');
        if (!is_object($provider) || !method_exists($provider, 'generate')) {
            $provider = new AIWM_Web_Gemini_Provider();
        }
        $suggestions = $provider->generate($candidates);
        if (is_wp_error($suggestions)) {
            return $suggestions;
        }
        if (self::job_cancel_requested((int) $job['id'])) {
            return self::error('aiwm_job_cancelled', __('AI recommendation job was cancelled before suggestions were persisted.', 'aiwm-web'), 409);
        }

        $wpdb->query('START TRANSACTION');
        try {
            foreach ($suggestions as $suggestion) {
                $key = self::find_candidate($candidates, $suggestion);
                if (!$key) {
                    continue;
                }
                $now = current_time('mysql', true);
                $ok = $wpdb->insert(self::table('recommendations'), [
                    'site_id' => (int) $job['site_id'],
                    'audit_id' => $auditId,
                    'object_type' => $suggestion['object_type'],
                    'object_id' => (string) $suggestion['object_id'],
                    'risk' => $suggestion['risk'],
                    'status' => 'pending',
                    'before_json' => wp_json_encode(['field' => $suggestion['field'], 'value' => $key['current']]),
                    'proposed_json' => wp_json_encode([
                        'field' => $suggestion['field'],
                        'value' => $suggestion['proposed'],
                        'reason' => $suggestion['reason'],
                        'provider' => $suggestion['provider'],
                        'model' => $suggestion['model'],
                    ]),
                    'created_at' => $now,
                    'updated_at' => $now,
                ]);
                if (!$ok) {
                    throw new RuntimeException('recommendation_insert_failed');
                }
            }
            $wpdb->query('COMMIT');
        } catch (Throwable $error) {
            $wpdb->query('ROLLBACK');
            return self::error('aiwm_recommendation_persist_failed', __('AI recommendations could not be persisted atomically.', 'aiwm-web'), 500);
        }
        return ['recommendation_count' => count($suggestions), 'provider' => 'Gemini', 'generated_at' => gmdate('c')];
    }

    private static function process_execution(array $job)
    {
        global $wpdb;
        $payload = self::decode_json($job['payload_json']);
        $recommendationId = absint($payload['recommendation_id'] ?? 0);
        $recommendation = self::get_recommendation($recommendationId);
        if (!$recommendation) {
            return self::error('aiwm_recommendation_not_found', __('Suggested change was not found.', 'aiwm-web'), 404);
        }
        if (!in_array($recommendation['status'], ['approved', 'executed'], true)) {
            return self::error('aiwm_recommendation_not_approved', __('Execution requires persisted human approval.', 'aiwm-web'), 409);
        }
        $site = self::verified_site((int) $recommendation['site_id']);
        if (is_wp_error($site)) {
            return $site;
        }

        $beforeSpec = self::decode_json($recommendation['before_json']);
        $proposedSpec = self::decode_json($recommendation['proposed_json']);
        $field = sanitize_key($proposedSpec['field'] ?? '');
        $proposedValue = is_string($proposedSpec['value'] ?? null) ? $proposedSpec['value'] : '';
        if (!in_array($field, ['title', 'excerpt', 'slug'], true) || $proposedValue === '') {
            return self::error('aiwm_execution_payload_invalid', __('Suggested change contains no supported mutation.', 'aiwm-web'), 409);
        }
        $type = sanitize_key($recommendation['object_type']);
        $objectId = absint($recommendation['object_id']);
        if (!in_array($type, ['posts', 'pages'], true) || !$objectId) {
            return self::error('aiwm_execution_object_invalid', __('Suggested change points to an unsupported WordPress object.', 'aiwm-web'), 409);
        }

        $identity = self::remote_request($site, 'GET', '/wp-json/');
        if (is_wp_error($identity)) {
            return $identity;
        }
        $expectedHost = strtolower((string) wp_parse_url($site['base_url'], PHP_URL_HOST));
        $reportedHost = strtolower((string) wp_parse_url((string) ($identity['url'] ?? $identity['home'] ?? ''), PHP_URL_HOST));
        if ($reportedHost !== '' && $reportedHost !== $expectedHost) {
            return self::error('aiwm_target_identity_mismatch', __('Target identity changed before execution.', 'aiwm-web'), 409);
        }

        $resource = self::remote_request($site, 'GET', '/wp-json/wp/v2/' . $type . '/' . $objectId . '?context=edit');
        if (is_wp_error($resource)) {
            return $resource;
        }
        $currentValue = self::resource_field($resource, $field);
        $expectedBefore = is_string($beforeSpec['value'] ?? null) ? $beforeSpec['value'] : '';

        $execution = $wpdb->get_row($wpdb->prepare(
            'SELECT * FROM ' . self::table('executions') . ' WHERE recommendation_id = %d ORDER BY id DESC LIMIT 1',
            $recommendationId
        ), ARRAY_A);

        if ($currentValue === $proposedValue) {
            if (!$execution) {
                $execution = self::create_execution($site, $recommendation, $job, $resource);
                if (is_wp_error($execution)) {
                    return $execution;
                }
            }
            return self::complete_execution($execution, $recommendation, $resource, $resource, true);
        }
        if ($currentValue !== $expectedBefore) {
            return self::error('aiwm_execution_stale_source', __('Remote content changed after approval; execution was blocked.', 'aiwm-web'), 409);
        }

        if (!$execution || $execution['status'] === 'completed') {
            $execution = self::create_execution($site, $recommendation, $job, $resource);
            if (is_wp_error($execution)) {
                return $execution;
            }
        } else {
            $wpdb->update(self::table('executions'), [
                'status' => 'running',
                'error_json' => null,
            ], ['id' => (int) $execution['id']]);
            $execution = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('executions') . ' WHERE id = %d', (int) $execution['id']), ARRAY_A);
        }

        if (self::job_cancel_requested((int) $job['id'])) {
            return self::error('aiwm_job_cancelled', __('Execution was cancelled before the remote mutation.', 'aiwm-web'), 409);
        }

        $mutation = self::remote_request($site, 'POST', '/wp-json/wp/v2/' . $type . '/' . $objectId, [$field => $proposedValue]);
        if (is_wp_error($mutation)) {
            self::fail_execution($execution, $mutation);
            return $mutation;
        }
        $verified = self::remote_request($site, 'GET', '/wp-json/wp/v2/' . $type . '/' . $objectId . '?context=edit');
        if (is_wp_error($verified)) {
            self::fail_execution($execution, $verified);
            return $verified;
        }
        if (self::resource_field($verified, $field) !== $proposedValue) {
            $error = self::error('aiwm_execution_verification_failed', __('Remote verification did not match the approved change.', 'aiwm-web'), 502);
            self::fail_execution($execution, $error, $verified);
            return $error;
        }
        return self::complete_execution($execution, $recommendation, $resource, $verified, false);
    }

    private static function complete_execution(array $execution, array $recommendation, array $before, array $after, bool $alreadyApplied): array
    {
        global $wpdb;
        $verification = [
            'verified' => true,
            'already_applied' => $alreadyApplied,
            'verified_at' => gmdate('c'),
            'object_type' => $recommendation['object_type'],
            'object_id' => absint($recommendation['object_id']),
        ];
        $wpdb->update(self::table('executions'), [
            'status' => 'completed',
            'after_json' => wp_json_encode($after),
            'verification_json' => wp_json_encode($verification),
            'error_json' => null,
            'completed_at' => current_time('mysql', true),
        ], ['id' => (int) $execution['id']]);
        $wpdb->update(self::table('recommendations'), [
            'status' => 'executed',
            'updated_at' => current_time('mysql', true),
        ], ['id' => (int) $recommendation['id']]);
        self::add_evidence((int) $recommendation['site_id'], (int) $execution['id'], 'after_snapshot', $after);
        self::add_evidence((int) $recommendation['site_id'], (int) $execution['id'], 'receipt', [
            'recommendation_id' => (int) $recommendation['id'],
            'execution_id' => (int) $execution['id'],
            'status' => 'completed',
            'verification' => $verification,
            'completed_at' => gmdate('c'),
        ]);
        return ['execution_id' => (int) $execution['id'], 'verified' => true, 'already_applied' => $alreadyApplied];
    }

    private static function create_execution(array $site, array $recommendation, array $job, array $before)
    {
        global $wpdb;
        $ok = $wpdb->insert(self::table('executions'), [
            'site_id' => (int) $site['id'],
            'recommendation_id' => (int) $recommendation['id'],
            'job_id' => (int) $job['id'],
            'status' => 'running',
            'before_json' => wp_json_encode($before),
            'after_json' => null,
            'verification_json' => null,
            'error_json' => null,
            'created_at' => current_time('mysql', true),
            'completed_at' => null,
        ]);
        if (!$ok) {
            return self::error('aiwm_execution_insert_failed', __('Unable to create a persisted execution record.', 'aiwm-web'), 500);
        }
        $id = (int) $wpdb->insert_id;
        self::add_evidence((int) $site['id'], $id, 'before_snapshot', $before);
        return $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('executions') . ' WHERE id = %d', $id), ARRAY_A);
    }

    private static function fail_execution(array $execution, WP_Error $error, ?array $after = null): void
    {
        global $wpdb;
        $payload = [
            'code' => $error->get_error_code(),
            'message' => $error->get_error_message(),
            'data' => self::safe_error_data($error->get_error_data()),
        ];
        $wpdb->update(self::table('executions'), [
            'status' => 'failed',
            'after_json' => $after ? wp_json_encode($after) : null,
            'error_json' => wp_json_encode($payload),
            'completed_at' => current_time('mysql', true),
        ], ['id' => (int) $execution['id']]);
        self::add_evidence((int) $execution['site_id'], (int) $execution['id'], 'failure', $payload);
    }

    private static function add_evidence(int $siteId, int $executionId, string $kind, array $metadata): void
    {
        global $wpdb;
        $encoded = wp_json_encode($metadata, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        $hash = hash('sha256', (string) $encoded);
        $wpdb->insert(self::table('evidence'), [
            'site_id' => $siteId,
            'execution_id' => $executionId,
            'kind' => sanitize_key($kind),
            'storage_key' => 'db://aiwm/' . sanitize_key($kind) . '/' . $executionId . '/' . substr($hash, 0, 16),
            'sha256' => $hash,
            'metadata_json' => $encoded,
            'created_at' => current_time('mysql', true),
        ]);
    }

    private static function analyze_resource(string $type, array $item): array
    {
        $id = absint($item['id'] ?? 0);
        $title = self::resource_field($item, 'title');
        $excerpt = self::resource_field($item, 'excerpt');
        $content = self::resource_field($item, 'content');
        $slug = self::resource_field($item, 'slug');
        $findings = [];
        $checks = 5;
        $passed = 0;

        $titleLength = self::text_length(wp_strip_all_tags($title));
        if ($titleLength >= 20 && $titleLength <= 70) {
            $passed++;
        } else {
            $findings[] = self::finding($type, $id, 'title', $title, 'title_length', 'medium', sprintf(__('Title length is %d characters; target range is 20–70 for this demo audit.', 'aiwm-web'), $titleLength));
        }

        if (trim(wp_strip_all_tags($excerpt)) !== '') {
            $passed++;
        } else {
            $findings[] = self::finding($type, $id, 'excerpt', $excerpt, 'excerpt_missing', 'low', __('Excerpt is empty.', 'aiwm-web'));
        }

        $wordCount = self::word_count(wp_strip_all_tags($content));
        if ($wordCount >= 120) {
            $passed++;
        } else {
            $findings[] = self::finding($type, $id, 'content', $content, 'thin_content', 'medium', sprintf(__('Content has %d words; this demo quality signal expects at least 120.', 'aiwm-web'), $wordCount));
        }

        $slugLength = self::text_length($slug);
        if ($slug !== '' && $slugLength <= 75) {
            $passed++;
        } else {
            $findings[] = self::finding($type, $id, 'slug', $slug, 'slug_quality', 'low', __('Slug is empty or longer than 75 characters.', 'aiwm-web'));
        }

        preg_match_all('/<h1\b/i', $content, $matches);
        $h1Count = count($matches[0] ?? []);
        if ($h1Count <= 1) {
            $passed++;
        } else {
            $findings[] = self::finding($type, $id, 'content', $content, 'multiple_h1', 'low', sprintf(__('Content contains %d H1 headings; expected at most one in post content.', 'aiwm-web'), $h1Count));
        }

        return ['checks' => $checks, 'passed' => $passed, 'findings' => $findings];
    }

    private static function finding(string $type, int $id, string $field, string $current, string $code, string $severity, string $message): array
    {
        return [
            'code' => $code,
            'severity' => $severity,
            'object_type' => $type,
            'object_id' => $id,
            'field' => $field,
            'current' => $field === 'content' ? '' : $current,
            'message' => $message,
        ];
    }

    private static function fetch_resources(array $site, string $type, int $page, int $perPage)
    {
        $path = sprintf('/wp-json/wp/v2/%s?context=edit&page=%d&per_page=%d&orderby=id&order=asc', $type, $page, $perPage);
        $response = self::remote_request($site, 'GET', $path);
        if (is_wp_error($response)) {
            if ($response->get_error_code() === 'aiwm_remote_http_400' && $page > 1) {
                return [];
            }
            return $response;
        }
        return is_array($response) ? array_values($response) : [];
    }

    private static function remote_request(array $site, string $method, string $path, ?array $body = null)
    {
        $credentials = self::resolve_credential((string) ($site['credential_ref'] ?? ''));
        if (is_wp_error($credentials)) {
            return $credentials;
        }
        $baseUrl = rtrim((string) $site['base_url'], '/');
        $url = $baseUrl . '/' . ltrim($path, '/');
        $args = [
            'method' => strtoupper($method),
            'timeout' => 20,
            'redirection' => 2,
            'headers' => [
                'Accept' => 'application/json',
                'Authorization' => 'Basic ' . base64_encode($credentials['username'] . ':' . $credentials['application_password']),
            ],
        ];
        if ($body !== null) {
            $args['headers']['Content-Type'] = 'application/json';
            $args['body'] = wp_json_encode($body);
        }
        $response = wp_safe_remote_request($url, $args);
        if (is_wp_error($response)) {
            return self::error('aiwm_remote_unavailable', __('Remote WordPress request failed.', 'aiwm-web'), 502, [
                'technical' => sanitize_text_field($response->get_error_message()),
            ]);
        }
        $status = (int) wp_remote_retrieve_response_code($response);
        $raw = (string) wp_remote_retrieve_body($response);
        $decoded = json_decode($raw, true);
        if ($status < 200 || $status >= 300) {
            $message = is_array($decoded) && is_string($decoded['message'] ?? null)
                ? sanitize_text_field($decoded['message'])
                : __('Remote WordPress rejected the request.', 'aiwm-web');
            return self::error('aiwm_remote_http_' . $status, $message, $status >= 400 && $status < 600 ? $status : 502);
        }
        if (!is_array($decoded)) {
            return self::error('aiwm_remote_malformed_response', __('Remote WordPress returned invalid JSON.', 'aiwm-web'), 502);
        }
        return $decoded;
    }

    private static function resolve_credential(string $reference)
    {
        $resolved = apply_filters('aiwm_web_remote_credential', null, $reference);
        if (is_array($resolved) && self::credential_valid($resolved)) {
            return $resolved;
        }
        if (defined('AIWM_REMOTE_CREDENTIALS') && is_array(AIWM_REMOTE_CREDENTIALS)) {
            $candidate = AIWM_REMOTE_CREDENTIALS[$reference] ?? null;
            if (is_array($candidate) && self::credential_valid($candidate)) {
                return $candidate;
            }
        }
        return self::error('aiwm_remote_credential_missing', __('Remote credential reference is not configured on the server.', 'aiwm-web'), 503);
    }

    private static function credential_valid(array $credential): bool
    {
        return is_string($credential['username'] ?? null)
            && trim($credential['username']) !== ''
            && is_string($credential['application_password'] ?? null)
            && trim($credential['application_password']) !== '';
    }

    private static function queue_job(int $siteId, string $type, array $payload, string $idempotencyKey)
    {
        global $wpdb;
        $existing = $wpdb->get_row($wpdb->prepare(
            'SELECT * FROM ' . self::table('jobs') . ' WHERE idempotency_key = %s LIMIT 1',
            $idempotencyKey
        ), ARRAY_A);
        if ($existing) {
            return $existing;
        }
        $payload['attempt'] = absint($payload['attempt'] ?? 1);
        $ok = $wpdb->insert(self::table('jobs'), [
            'site_id' => $siteId ?: null,
            'type' => sanitize_key($type),
            'status' => 'queued',
            'idempotency_key' => $idempotencyKey,
            'progress_current' => 0,
            'progress_total' => 1,
            'cursor_json' => null,
            'payload_json' => wp_json_encode($payload),
            'error_json' => null,
            'created_at' => current_time('mysql', true),
            'started_at' => null,
            'finished_at' => null,
        ]);
        if (!$ok) {
            $existing = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('jobs') . ' WHERE idempotency_key = %s LIMIT 1', $idempotencyKey), ARRAY_A);
            if ($existing) {
                return $existing;
            }
            return self::error('aiwm_job_insert_failed', __('Unable to persist the functional journey job.', 'aiwm-web'), 500);
        }
        $jobId = (int) $wpdb->insert_id;
        self::schedule_job($jobId);
        return self::get_job($jobId);
    }

    private static function schedule_job(int $jobId): void
    {
        $args = [$jobId];
        if (!wp_next_scheduled(self::CRON_HOOK, $args)) {
            wp_schedule_single_event(time() + 1, self::CRON_HOOK, $args);
        }
    }

    private static function job_cancel_requested(int $jobId): bool
    {
        $job = self::get_job($jobId);
        return $job && in_array($job['status'], ['cancel_requested', 'cancelled'], true);
    }

    private static function verified_site(int $id)
    {
        $site = self::get_site($id);
        if (!$site) {
            return self::error('aiwm_site_not_found', __('Managed site was not found.', 'aiwm-web'), 404);
        }
        if ($site['status'] !== 'verified') {
            return self::error('aiwm_site_not_verified', __('Managed site must be verified before this operation.', 'aiwm-web'), 409);
        }
        return $site;
    }

    private static function get_site(int $id): ?array
    {
        global $wpdb;
        $row = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('sites') . ' WHERE id = %d', $id), ARRAY_A);
        return is_array($row) ? $row : null;
    }

    private static function get_job(int $id): ?array
    {
        global $wpdb;
        $row = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('jobs') . ' WHERE id = %d', $id), ARRAY_A);
        return is_array($row) ? $row : null;
    }

    private static function get_recommendation(int $id): ?array
    {
        global $wpdb;
        $row = $wpdb->get_row($wpdb->prepare('SELECT * FROM ' . self::table('recommendations') . ' WHERE id = %d', $id), ARRAY_A);
        return is_array($row) ? $row : null;
    }

    private static function mark_site_verification(int $id, string $status): void
    {
        global $wpdb;
        $wpdb->update(self::table('sites'), [
            'status' => sanitize_key($status),
            'updated_at' => current_time('mysql', true),
        ], ['id' => $id]);
    }

    private static function fail_audit(int $auditId, string $status = 'failed'): void
    {
        global $wpdb;
        $wpdb->update(self::table('audits'), [
            'status' => sanitize_key($status),
            'completed_at' => current_time('mysql', true),
        ], ['id' => $auditId]);
    }

    private static function normalize_base_url($value)
    {
        $url = esc_url_raw(trim((string) $value));
        $parts = wp_parse_url($url);
        if (!$url || !is_array($parts) || empty($parts['scheme']) || empty($parts['host'])) {
            return self::error('aiwm_site_url_invalid', __('A valid absolute WordPress base URL is required.', 'aiwm-web'), 400);
        }
        $scheme = strtolower((string) $parts['scheme']);
        if ($scheme !== 'https' && !(defined('AIWM_ALLOW_INSECURE_REMOTE') && AIWM_ALLOW_INSECURE_REMOTE === true && $scheme === 'http')) {
            return self::error('aiwm_site_https_required', __('Remote WordPress targets require HTTPS unless insecure remote access is explicitly enabled for a disposable test environment.', 'aiwm-web'), 400);
        }
        return trailingslashit($url);
    }

    private static function resource_field(array $item, string $field): string
    {
        if (in_array($field, ['title', 'excerpt', 'content'], true)) {
            $value = $item[$field] ?? '';
            if (is_array($value)) {
                $value = $value['raw'] ?? $value['rendered'] ?? '';
            }
            return is_string($value) ? $value : '';
        }
        $value = $item[$field] ?? '';
        return is_string($value) ? $value : '';
    }

    private static function public_resource(array $item): array
    {
        return [
            'id' => absint($item['id'] ?? 0),
            'type' => sanitize_key($item['type'] ?? ''),
            'status' => sanitize_key($item['status'] ?? ''),
            'slug' => self::resource_field($item, 'slug'),
            'title' => self::resource_field($item, 'title'),
            'excerpt' => self::resource_field($item, 'excerpt'),
            'modifiedGmt' => sanitize_text_field($item['modified_gmt'] ?? ''),
            'link' => esc_url_raw($item['link'] ?? ''),
        ];
    }

    private static function public_site(array $site): array
    {
        return [
            'id' => (int) $site['id'],
            'name' => $site['name'],
            'baseUrl' => $site['base_url'],
            'status' => $site['status'],
            'authType' => $site['auth_type'],
            'credentialRef' => $site['credential_ref'],
            'lastVerifiedAt' => self::mysql_to_iso($site['last_verified_at'] ?? null),
            'createdAt' => self::mysql_to_iso($site['created_at'] ?? null),
            'updatedAt' => self::mysql_to_iso($site['updated_at'] ?? null),
        ];
    }

    private static function public_audit(array $audit): array
    {
        return [
            'id' => (int) $audit['id'],
            'siteId' => (int) $audit['site_id'],
            'status' => $audit['status'],
            'score' => $audit['score'] === null ? null : (float) $audit['score'],
            'findingsCount' => (int) $audit['findings_count'],
            'startedAt' => self::mysql_to_iso($audit['started_at'] ?? null),
            'completedAt' => self::mysql_to_iso($audit['completed_at'] ?? null),
            'createdAt' => self::mysql_to_iso($audit['created_at'] ?? null),
        ];
    }

    private static function public_job(array $job): array
    {
        $payload = self::decode_json($job['payload_json']);
        $safePayload = $payload;
        unset($safePayload['snapshot']);
        return [
            'id' => (int) $job['id'],
            'siteId' => $job['site_id'] === null ? null : (int) $job['site_id'],
            'type' => $job['type'],
            'status' => $job['status'],
            'idempotencyKey' => $job['idempotency_key'],
            'progress' => ['current' => (int) $job['progress_current'], 'total' => (int) $job['progress_total']],
            'payload' => $safePayload,
            'error' => self::decode_json($job['error_json']),
            'createdAt' => self::mysql_to_iso($job['created_at'] ?? null),
            'startedAt' => self::mysql_to_iso($job['started_at'] ?? null),
            'finishedAt' => self::mysql_to_iso($job['finished_at'] ?? null),
        ];
    }

    private static function public_recommendation(array $row): array
    {
        return [
            'id' => (int) $row['id'],
            'siteId' => (int) $row['site_id'],
            'auditId' => $row['audit_id'] === null ? null : (int) $row['audit_id'],
            'objectType' => $row['object_type'],
            'objectId' => $row['object_id'],
            'risk' => $row['risk'],
            'status' => $row['status'],
            'before' => self::decode_json($row['before_json']),
            'proposed' => self::decode_json($row['proposed_json']),
            'createdAt' => self::mysql_to_iso($row['created_at'] ?? null),
            'updatedAt' => self::mysql_to_iso($row['updated_at'] ?? null),
        ];
    }

    private static function public_execution(array $row): array
    {
        return [
            'id' => (int) $row['id'],
            'siteId' => (int) $row['site_id'],
            'recommendationId' => $row['recommendation_id'] === null ? null : (int) $row['recommendation_id'],
            'jobId' => $row['job_id'] === null ? null : (int) $row['job_id'],
            'status' => $row['status'],
            'before' => self::decode_json($row['before_json']),
            'after' => self::decode_json($row['after_json']),
            'verification' => self::decode_json($row['verification_json']),
            'error' => self::decode_json($row['error_json']),
            'createdAt' => self::mysql_to_iso($row['created_at'] ?? null),
            'completedAt' => self::mysql_to_iso($row['completed_at'] ?? null),
        ];
    }

    private static function public_evidence(array $row): array
    {
        return [
            'id' => (int) $row['id'],
            'siteId' => (int) $row['site_id'],
            'executionId' => $row['execution_id'] === null ? null : (int) $row['execution_id'],
            'kind' => $row['kind'],
            'storageKey' => $row['storage_key'],
            'sha256' => $row['sha256'],
            'metadata' => self::decode_json($row['metadata_json']),
            'createdAt' => self::mysql_to_iso($row['created_at'] ?? null),
        ];
    }

    private static function find_candidate(array $candidates, array $suggestion): ?array
    {
        foreach ($candidates as $candidate) {
            if (($candidate['object_type'] ?? null) === ($suggestion['object_type'] ?? null)
                && absint($candidate['object_id'] ?? 0) === absint($suggestion['object_id'] ?? 0)
                && ($candidate['field'] ?? null) === ($suggestion['field'] ?? null)) {
                return $candidate;
            }
        }
        return null;
    }

    private static function table(string $name): string
    {
        global $wpdb;
        return $wpdb->prefix . 'aiwm_' . $name;
    }

    private static function decode_json($value): array
    {
        if (!is_string($value) || $value === '') {
            return [];
        }
        $decoded = json_decode($value, true);
        return is_array($decoded) ? $decoded : [];
    }

    private static function json_params(WP_REST_Request $request): array
    {
        $data = $request->get_json_params();
        return is_array($data) ? $data : [];
    }

    private static function error(string $code, string $message, int $status, array $data = []): WP_Error
    {
        return new WP_Error($code, $message, array_merge(['status' => $status], $data));
    }

    private static function safe_error_data($data): array
    {
        if (!is_array($data)) {
            return [];
        }
        unset($data['apiKey'], $data['api_key'], $data['application_password'], $data['password'], $data['authorization']);
        return $data;
    }

    private static function mysql_to_iso($value): ?string
    {
        if (!is_string($value) || $value === '' || $value === '0000-00-00 00:00:00') {
            return null;
        }
        $timestamp = strtotime($value . ' UTC');
        return $timestamp ? gmdate('c', $timestamp) : null;
    }

    private static function text_length(string $value): int
    {
        return function_exists('mb_strlen') ? mb_strlen($value, 'UTF-8') : strlen($value);
    }

    private static function word_count(string $value): int
    {
        $value = trim(preg_replace('/\s+/u', ' ', $value));
        if ($value === '') {
            return 0;
        }
        $parts = preg_split('/\s+/u', $value, -1, PREG_SPLIT_NO_EMPTY);
        return is_array($parts) ? count($parts) : 0;
    }
}
