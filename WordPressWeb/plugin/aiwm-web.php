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
    }

    public static function rest_can_read(): bool
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
}

AIWM_Web_Edition::boot();
