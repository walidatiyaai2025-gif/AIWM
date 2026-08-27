<?php
/**
 * Plugin Name: AI WordPress Manager Web Edition
 * Description: WordPress-hosted Web Edition of AI WordPress Manager.
 * Version: 0.1.0-dev
 * Requires at least: 6.5
 * Requires PHP: 8.1
 * Author: AIWM
 */

if (!defined('ABSPATH')) {
    exit;
}

final class AIWM_Web_Edition
{
    private const VERSION = '0.1.0-dev';
    private const REST_NAMESPACE = 'aiwm/v1';
    private const CAPABILITY = 'manage_aiwm';
    private const SITE_AUTH_TYPES = ['application_password', 'connector'];

    public static function boot(): void
    {
        register_activation_hook(__FILE__, [self::class, 'activate']);
        add_action('admin_menu', [self::class, 'register_admin_menu']);
        add_action('admin_enqueue_scripts', [self::class, 'enqueue_admin_assets']);
        add_action('rest_api_init', [self::class, 'register_rest_routes']);
    }

    public static function activate(): void
    {
        self::install_schema();

        $admin = get_role('administrator');
        if ($admin && !$admin->has_cap(self::CAPABILITY)) {
            $admin->add_cap(self::CAPABILITY);
        }

        update_option('aiwm_web_schema_version', '1');
        update_option('aiwm_web_version', self::VERSION);
    }

    private static function install_schema(): void
    {
        global $wpdb;
        require_once ABSPATH . 'wp-admin/includes/upgrade.php';

        $charset = $wpdb->get_charset_collate();
        $prefix = $wpdb->prefix . 'aiwm_';

        $tables = [
            "CREATE TABLE {$prefix}sites (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                name VARCHAR(190) NOT NULL,
                base_url VARCHAR(2048) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'pending',
                auth_type VARCHAR(32) NOT NULL DEFAULT 'application_password',
                credential_ref VARCHAR(190) NULL,
                last_verified_at DATETIME NULL,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY status (status)
            ) {$charset};",
            "CREATE TABLE {$prefix}audits (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                status VARCHAR(32) NOT NULL,
                score DECIMAL(5,2) NULL,
                findings_count INT UNSIGNED NOT NULL DEFAULT 0,
                started_at DATETIME NULL,
                completed_at DATETIME NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY site_status (site_id, status)
            ) {$charset};",
            "CREATE TABLE {$prefix}recommendations (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                audit_id BIGINT UNSIGNED NULL,
                object_type VARCHAR(64) NOT NULL,
                object_id VARCHAR(190) NOT NULL,
                risk VARCHAR(32) NOT NULL DEFAULT 'medium',
                status VARCHAR(32) NOT NULL DEFAULT 'pending',
                before_json LONGTEXT NULL,
                proposed_json LONGTEXT NOT NULL,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY site_status (site_id, status)
            ) {$charset};",
            "CREATE TABLE {$prefix}jobs (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NULL,
                type VARCHAR(64) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'queued',
                idempotency_key VARCHAR(190) NOT NULL,
                progress_current BIGINT UNSIGNED NOT NULL DEFAULT 0,
                progress_total BIGINT UNSIGNED NOT NULL DEFAULT 0,
                cursor_json LONGTEXT NULL,
                payload_json LONGTEXT NULL,
                error_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                started_at DATETIME NULL,
                finished_at DATETIME NULL,
                PRIMARY KEY (id),
                UNIQUE KEY idempotency_key (idempotency_key),
                KEY status_created (status, created_at)
            ) {$charset};",
            "CREATE TABLE {$prefix}executions (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                recommendation_id BIGINT UNSIGNED NULL,
                job_id BIGINT UNSIGNED NULL,
                status VARCHAR(32) NOT NULL,
                before_json LONGTEXT NULL,
                after_json LONGTEXT NULL,
                verification_json LONGTEXT NULL,
                error_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                completed_at DATETIME NULL,
                PRIMARY KEY (id),
                KEY site_status (site_id, status)
            ) {$charset};",
            "CREATE TABLE {$prefix}evidence (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                execution_id BIGINT UNSIGNED NULL,
                kind VARCHAR(64) NOT NULL,
                storage_key VARCHAR(255) NOT NULL,
                sha256 CHAR(64) NULL,
                metadata_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id),
                KEY execution_id (execution_id)
            ) {$charset};",
        ];

        foreach ($tables as $sql) {
            dbDelta($sql);
        }
    }

    public static function register_admin_menu(): void
    {
        add_menu_page(
            __('AI WordPress Manager', 'aiwm-web'),
            __('AI WordPress Manager', 'aiwm-web'),
            self::CAPABILITY,
            'aiwm-web',
            [self::class, 'render_admin_app'],
            'dashicons-admin-site-alt3',
            3
        );
    }

    public static function render_admin_app(): void
    {
        if (!current_user_can(self::CAPABILITY)) {
            wp_die(esc_html__('You do not have permission to access AI WordPress Manager.', 'aiwm-web'));
        }

        echo '<div class="wrap aiwm-web-host"><div id="aiwm-web-root" data-status="booting">';
        echo '<div class="aiwm-web-boot">';
        echo '<h1>' . esc_html__('AI WordPress Manager', 'aiwm-web') . '</h1>';
        echo '<p>' . esc_html__('Loading Web Edition…', 'aiwm-web') . '</p>';
        echo '</div></div></div>';
    }

    public static function enqueue_admin_assets(string $hook): void
    {
        if ($hook !== 'toplevel_page_aiwm-web') {
            return;
        }

        $base = plugin_dir_url(__FILE__);
        $path = plugin_dir_path(__FILE__);

        if (file_exists($path . 'assets/admin.css')) {
            wp_enqueue_style('aiwm-web-admin', $base . 'assets/admin.css', [], self::VERSION);
        }

        if (file_exists($path . 'assets/admin.js')) {
            wp_enqueue_script('aiwm-web-admin', $base . 'assets/admin.js', ['wp-api-fetch'], self::VERSION, true);
        }

        wp_add_inline_script(
            'wp-api-fetch',
            'window.AIWM_WEB_BOOTSTRAP = ' . wp_json_encode([
                'restRoot' => esc_url_raw(rest_url(self::REST_NAMESPACE . '/')),
                'nonce' => wp_create_nonce('wp_rest'),
                'locale' => determine_locale(),
                'isRtl' => is_rtl(),
                'version' => self::VERSION,
            ]) . ';',
            'before'
        );
    }

    public static function register_rest_routes(): void
    {
        register_rest_route(self::REST_NAMESPACE, '/health', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_health'],
            'permission_callback' => [self::class, 'rest_can_read'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/dashboard', [
            'methods' => WP_REST_Server::READABLE,
            'callback' => [self::class, 'rest_dashboard'],
            'permission_callback' => [self::class, 'rest_can_read'],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/sites', [
            [
                'methods' => WP_REST_Server::READABLE,
                'callback' => [self::class, 'rest_sites_index'],
                'permission_callback' => [self::class, 'rest_can_read'],
            ],
            [
                'methods' => WP_REST_Server::CREATABLE,
                'callback' => [self::class, 'rest_sites_create'],
                'permission_callback' => [self::class, 'rest_can_mutate'],
            ],
        ]);

        register_rest_route(self::REST_NAMESPACE, '/sites/(?P<id>\d+)', [
            [
                'methods' => WP_REST_Server::READABLE,
                'callback' => [self::class, 'rest_sites_show'],
                'permission_callback' => [self::class, 'rest_can_read'],
            ],
            [
                'methods' => WP_REST_Server::EDITABLE,
                'callback' => [self::class, 'rest_sites_update'],
                'permission_callback' => [self::class, 'rest_can_mutate'],
            ],
            [
                'methods' => WP_REST_Server::DELETABLE,
                'callback' => [self::class, 'rest_sites_delete'],
                'permission_callback' => [self::class, 'rest_can_mutate'],
            ],
        ]);
    }

    public static function rest_can_read(): bool
    {
        return current_user_can(self::CAPABILITY);
    }

    public static function rest_can_mutate(): bool
    {
        return current_user_can(self::CAPABILITY);
    }

    public static function rest_health(): WP_REST_Response
    {
        global $wpdb;
        $prefix = $wpdb->prefix . 'aiwm_';
        $required = ['sites', 'audits', 'recommendations', 'jobs', 'executions', 'evidence'];
        $tables = [];

        foreach ($required as $table) {
            $full = $prefix . $table;
            $tables[$table] = $wpdb->get_var($wpdb->prepare('SHOW TABLES LIKE %s', $full)) === $full;
        }

        return new WP_REST_Response([
            'ok' => !in_array(false, $tables, true),
            'version' => self::VERSION,
            'schemaVersion' => get_option('aiwm_web_schema_version'),
            'tables' => $tables,
            'timestamp' => gmdate('c'),
        ]);
    }

    public static function rest_dashboard(): WP_REST_Response
    {
        global $wpdb;
        $prefix = $wpdb->prefix . 'aiwm_';

        $sites = (int) $wpdb->get_var("SELECT COUNT(*) FROM {$prefix}sites");
        $verifiedSites = (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$prefix}sites WHERE status = %s", 'verified'));
        $pendingRecommendations = (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$prefix}recommendations WHERE status = %s", 'pending'));
        $runningJobs = (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$prefix}jobs WHERE status IN (%s, %s)", 'queued', 'running'));
        $failedExecutions = (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$prefix}executions WHERE status = %s", 'failed'));

        return new WP_REST_Response([
            'counts' => [
                'sites' => $sites,
                'verifiedSites' => $verifiedSites,
                'pendingRecommendations' => $pendingRecommendations,
                'runningJobs' => $runningJobs,
                'failedExecutions' => $failedExecutions,
            ],
            'generatedAt' => gmdate('c'),
        ]);
    }

    public static function rest_sites_index(WP_REST_Request $request): WP_REST_Response
    {
        global $wpdb;
        $table = $wpdb->prefix . 'aiwm_sites';
        $page = max(1, (int) $request->get_param('page'));
        $perPage = min(100, max(1, (int) ($request->get_param('per_page') ?: 25)));
        $offset = ($page - 1) * $perPage;
        $status = sanitize_key((string) $request->get_param('status'));
        $search = sanitize_text_field((string) $request->get_param('search'));

        $where = [];
        $args = [];

        if ($status !== '') {
            $allowedStatuses = ['pending', 'verified', 'failed', 'disabled'];
            if (!in_array($status, $allowedStatuses, true)) {
                return new WP_REST_Response(['code' => 'invalid_status', 'message' => 'Unsupported site status.'], 400);
            }
            $where[] = 'status = %s';
            $args[] = $status;
        }

        if ($search !== '') {
            $like = '%' . $wpdb->esc_like($search) . '%';
            $where[] = '(name LIKE %s OR base_url LIKE %s)';
            $args[] = $like;
            $args[] = $like;
        }

        $whereSql = $where ? ' WHERE ' . implode(' AND ', $where) : '';
        $countSql = "SELECT COUNT(*) FROM {$table}{$whereSql}";
        $total = $args
            ? (int) $wpdb->get_var($wpdb->prepare($countSql, $args))
            : (int) $wpdb->get_var($countSql);

        $queryArgs = $args;
        $queryArgs[] = $perPage;
        $queryArgs[] = $offset;
        $rows = $wpdb->get_results(
            $wpdb->prepare(
                "SELECT id, name, base_url, status, auth_type, last_verified_at, created_at, updated_at
                 FROM {$table}{$whereSql}
                 ORDER BY id DESC
                 LIMIT %d OFFSET %d",
                $queryArgs
            ),
            ARRAY_A
        );

        $response = new WP_REST_Response([
            'items' => array_map([self::class, 'public_site_row'], $rows ?: []),
            'page' => $page,
            'perPage' => $perPage,
            'total' => $total,
            'totalPages' => $perPage > 0 ? (int) ceil($total / $perPage) : 0,
        ]);
        $response->header('X-WP-Total', (string) $total);
        $response->header('X-WP-TotalPages', (string) ($perPage > 0 ? (int) ceil($total / $perPage) : 0));

        return $response;
    }

    public static function rest_sites_show(WP_REST_Request $request)
    {
        $row = self::find_site((int) $request['id']);
        if (!$row) {
            return new WP_Error('aiwm_site_not_found', 'Managed site not found.', ['status' => 404]);
        }

        return new WP_REST_Response(self::public_site_row($row));
    }

    public static function rest_sites_create(WP_REST_Request $request)
    {
        global $wpdb;
        $table = $wpdb->prefix . 'aiwm_sites';

        $payload = self::site_payload($request, true);
        if (is_wp_error($payload)) {
            return $payload;
        }

        $existing = $wpdb->get_var($wpdb->prepare("SELECT id FROM {$table} WHERE base_url = %s LIMIT 1", $payload['base_url']));
        if ($existing) {
            return new WP_Error('aiwm_site_exists', 'This WordPress site is already managed.', ['status' => 409]);
        }

        $now = current_time('mysql', true);
        $inserted = $wpdb->insert(
            $table,
            [
                'name' => $payload['name'],
                'base_url' => $payload['base_url'],
                'status' => 'pending',
                'auth_type' => $payload['auth_type'],
                'credential_ref' => null,
                'created_at' => $now,
                'updated_at' => $now,
            ],
            ['%s', '%s', '%s', '%s', '%s', '%s', '%s']
        );

        if ($inserted !== 1) {
            return new WP_Error('aiwm_site_create_failed', 'Unable to create managed site.', ['status' => 500]);
        }

        $row = self::find_site((int) $wpdb->insert_id);
        return new WP_REST_Response(self::public_site_row($row), 201);
    }

    public static function rest_sites_update(WP_REST_Request $request)
    {
        global $wpdb;
        $table = $wpdb->prefix . 'aiwm_sites';
        $id = (int) $request['id'];
        $existing = self::find_site($id);

        if (!$existing) {
            return new WP_Error('aiwm_site_not_found', 'Managed site not found.', ['status' => 404]);
        }

        $payload = self::site_payload($request, false);
        if (is_wp_error($payload)) {
            return $payload;
        }

        $changes = ['updated_at' => current_time('mysql', true)];
        $formats = ['%s'];

        foreach (['name', 'base_url', 'auth_type'] as $field) {
            if (array_key_exists($field, $payload)) {
                $changes[$field] = $payload[$field];
                $formats[] = '%s';
            }
        }

        if (isset($changes['base_url']) && $changes['base_url'] !== $existing['base_url']) {
            $duplicate = $wpdb->get_var(
                $wpdb->prepare(
                    "SELECT id FROM {$table} WHERE base_url = %s AND id <> %d LIMIT 1",
                    $changes['base_url'],
                    $id
                )
            );
            if ($duplicate) {
                return new WP_Error('aiwm_site_exists', 'This WordPress site is already managed.', ['status' => 409]);
            }

            $changes['status'] = 'pending';
            $changes['last_verified_at'] = null;
            $formats[] = '%s';
            $formats[] = '%s';
        }

        if (count($changes) === 1) {
            return new WP_REST_Response(self::public_site_row($existing));
        }

        $updated = $wpdb->update($table, $changes, ['id' => $id], $formats, ['%d']);
        if ($updated === false) {
            return new WP_Error('aiwm_site_update_failed', 'Unable to update managed site.', ['status' => 500]);
        }

        return new WP_REST_Response(self::public_site_row(self::find_site($id)));
    }

    public static function rest_sites_delete(WP_REST_Request $request)
    {
        global $wpdb;
        $table = $wpdb->prefix . 'aiwm_sites';
        $id = (int) $request['id'];
        $existing = self::find_site($id);

        if (!$existing) {
            return new WP_Error('aiwm_site_not_found', 'Managed site not found.', ['status' => 404]);
        }

        $dependentTables = ['audits', 'recommendations', 'jobs', 'executions', 'evidence'];
        foreach ($dependentTables as $suffix) {
            $dependent = $wpdb->prefix . 'aiwm_' . $suffix;
            $count = (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$dependent} WHERE site_id = %d", $id));
            if ($count > 0) {
                return new WP_Error(
                    'aiwm_site_has_history',
                    'Managed site cannot be removed while audit, execution, job, or evidence history exists.',
                    ['status' => 409]
                );
            }
        }

        $deleted = $wpdb->delete($table, ['id' => $id], ['%d']);
        if ($deleted !== 1) {
            return new WP_Error('aiwm_site_delete_failed', 'Unable to remove managed site.', ['status' => 500]);
        }

        return new WP_REST_Response(['deleted' => true, 'id' => $id]);
    }

    private static function site_payload(WP_REST_Request $request, bool $creating)
    {
        $payload = [];

        if ($creating || $request->has_param('name')) {
            $name = sanitize_text_field((string) $request->get_param('name'));
            if ($name === '') {
                return new WP_Error('aiwm_invalid_site_name', 'Site name is required.', ['status' => 400]);
            }
            if (strlen($name) > 190) {
                return new WP_Error('aiwm_invalid_site_name', 'Site name is too long.', ['status' => 400]);
            }
            $payload['name'] = $name;
        }

        if ($creating || $request->has_param('base_url')) {
            $baseUrl = self::normalize_base_url((string) $request->get_param('base_url'));
            if (is_wp_error($baseUrl)) {
                return $baseUrl;
            }
            $payload['base_url'] = $baseUrl;
        }

        if ($creating || $request->has_param('auth_type')) {
            $authType = sanitize_key((string) ($request->get_param('auth_type') ?: 'application_password'));
            if (!in_array($authType, self::SITE_AUTH_TYPES, true)) {
                return new WP_Error('aiwm_invalid_auth_type', 'Unsupported authentication type.', ['status' => 400]);
            }
            $payload['auth_type'] = $authType;
        }

        return $payload;
    }

    private static function normalize_base_url(string $url)
    {
        $url = trim($url);
        if ($url === '') {
            return new WP_Error('aiwm_invalid_site_url', 'Site URL is required.', ['status' => 400]);
        }

        $url = esc_url_raw($url, ['http', 'https']);
        if (!$url || !wp_http_validate_url($url)) {
            return new WP_Error('aiwm_invalid_site_url', 'Enter a valid HTTP or HTTPS WordPress URL.', ['status' => 400]);
        }

        $parts = wp_parse_url($url);
        if (!$parts || empty($parts['scheme']) || empty($parts['host']) || isset($parts['user']) || isset($parts['pass'])) {
            return new WP_Error('aiwm_invalid_site_url', 'Site URL must not contain embedded credentials.', ['status' => 400]);
        }

        return untrailingslashit($url);
    }

    private static function find_site(int $id): ?array
    {
        global $wpdb;
        if ($id < 1) {
            return null;
        }

        $table = $wpdb->prefix . 'aiwm_sites';
        $row = $wpdb->get_row(
            $wpdb->prepare(
                "SELECT id, name, base_url, status, auth_type, last_verified_at, created_at, updated_at
                 FROM {$table}
                 WHERE id = %d",
                $id
            ),
            ARRAY_A
        );

        return is_array($row) ? $row : null;
    }

    private static function public_site_row(array $row): array
    {
        return [
            'id' => (int) $row['id'],
            'name' => (string) $row['name'],
            'baseUrl' => (string) $row['base_url'],
            'status' => (string) $row['status'],
            'authType' => (string) $row['auth_type'],
            'lastVerifiedAt' => $row['last_verified_at'] ?: null,
            'createdAt' => (string) $row['created_at'],
            'updatedAt' => (string) $row['updated_at'],
        ];
    }
}

AIWM_Web_Edition::boot();
