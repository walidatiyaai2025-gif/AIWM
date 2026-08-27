<?php
if (!defined('ABSPATH')) { exit; }

final class AIWM_Web_Store
{
    public const SCHEMA_VERSION = '3';
    private const CACHE_GROUP = 'aiwm_web';

    public static function table(string $name): string
    {
        global $wpdb;
        return $wpdb->prefix . 'aiwm_' . $name;
    }

    public static function maybe_migrate(): void
    {
        if ((string) get_option('aiwm_web_schema_version', '') !== self::SCHEMA_VERSION) {
            self::install_schema();
        }
    }

    public static function install_schema(): void
    {
        global $wpdb;
        require_once ABSPATH . 'wp-admin/includes/upgrade.php';
        $c = $wpdb->get_charset_collate();
        $p = $wpdb->prefix . 'aiwm_';
        $sql = [
            "CREATE TABLE {$p}sites (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                name VARCHAR(190) NOT NULL,
                base_url VARCHAR(2048) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'pending',
                auth_type VARCHAR(32) NOT NULL DEFAULT 'application_password',
                credential_ref VARCHAR(190) NULL,
                identity_fingerprint CHAR(64) NULL,
                last_verified_at DATETIME NULL,
                created_at DATETIME NOT NULL,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY status (status), KEY updated_at (updated_at)
            ) {$c};",
            "CREATE TABLE {$p}site_state (
                site_id BIGINT UNSIGNED NOT NULL,
                sync_status VARCHAR(32) NOT NULL DEFAULT 'idle',
                sync_cursor LONGTEXT NULL,
                wp_version VARCHAR(64) NULL,
                home_url VARCHAR(2048) NULL,
                content_hash CHAR(64) NULL,
                last_synced_at DATETIME NULL,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (site_id), KEY sync_status (sync_status)
            ) {$c};",
            "CREATE TABLE {$p}explorer_snapshots (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                resource_type VARCHAR(64) NOT NULL,
                resource_id VARCHAR(190) NOT NULL,
                resource_version VARCHAR(190) NULL,
                payload_json LONGTEXT NOT NULL,
                payload_hash CHAR(64) NOT NULL,
                captured_at DATETIME NOT NULL,
                PRIMARY KEY (id), UNIQUE KEY site_resource (site_id, resource_type, resource_id),
                KEY site_captured (site_id, captured_at)
            ) {$c};",
            "CREATE TABLE {$p}seo_audits (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'queued',
                score DECIMAL(5,2) NULL,
                findings_count INT UNSIGNED NOT NULL DEFAULT 0,
                job_id BIGINT UNSIGNED NULL,
                started_at DATETIME NULL, completed_at DATETIME NULL, created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY site_status (site_id, status), KEY job_id (job_id)
            ) {$c};",
            "CREATE TABLE {$p}findings (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                audit_id BIGINT UNSIGNED NOT NULL,
                severity VARCHAR(32) NOT NULL,
                rule_key VARCHAR(190) NOT NULL,
                object_type VARCHAR(64) NOT NULL,
                object_id VARCHAR(190) NOT NULL,
                summary TEXT NOT NULL,
                details_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY audit_severity (audit_id, severity), KEY site_object (site_id, object_type, object_id)
            ) {$c};",
            "CREATE TABLE {$p}suggested_changes (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                audit_id BIGINT UNSIGNED NULL,
                finding_id BIGINT UNSIGNED NULL,
                object_type VARCHAR(64) NOT NULL,
                object_id VARCHAR(190) NOT NULL,
                risk VARCHAR(32) NOT NULL DEFAULT 'medium',
                status VARCHAR(32) NOT NULL DEFAULT 'pending',
                before_json LONGTEXT NOT NULL,
                proposed_json LONGTEXT NOT NULL,
                version_hash CHAR(64) NOT NULL,
                created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY site_status (site_id, status), KEY audit_id (audit_id)
            ) {$c};",
            "CREATE TABLE {$p}approval_decisions (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                suggested_change_id BIGINT UNSIGNED NOT NULL,
                site_id BIGINT UNSIGNED NOT NULL,
                decision VARCHAR(32) NOT NULL,
                decision_version_hash CHAR(64) NOT NULL,
                decided_by BIGINT UNSIGNED NOT NULL,
                note TEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY change_created (suggested_change_id, created_at), KEY site_decision (site_id, decision)
            ) {$c};",
            "CREATE TABLE {$p}jobs (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NULL,
                type VARCHAR(64) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'queued',
                idempotency_key VARCHAR(190) NOT NULL,
                progress_current BIGINT UNSIGNED NOT NULL DEFAULT 0,
                progress_total BIGINT UNSIGNED NOT NULL DEFAULT 0,
                cursor_json LONGTEXT NULL, payload_json LONGTEXT NULL, error_json LONGTEXT NULL,
                attempts INT UNSIGNED NOT NULL DEFAULT 0,
                max_attempts INT UNSIGNED NOT NULL DEFAULT 5,
                lock_token CHAR(36) NULL, locked_at DATETIME NULL, last_heartbeat_at DATETIME NULL,
                next_attempt_at DATETIME NULL, cancel_requested_at DATETIME NULL,
                created_at DATETIME NOT NULL, started_at DATETIME NULL, finished_at DATETIME NULL,
                PRIMARY KEY (id), UNIQUE KEY idempotency_key (idempotency_key),
                KEY status_next (status, next_attempt_at), KEY site_status (site_id, status)
            ) {$c};",
            "CREATE TABLE {$p}job_items (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                job_id BIGINT UNSIGNED NOT NULL,
                item_key VARCHAR(190) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'queued',
                checkpoint_json LONGTEXT NULL, result_json LONGTEXT NULL, error_json LONGTEXT NULL,
                attempts INT UNSIGNED NOT NULL DEFAULT 0,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (id), UNIQUE KEY job_item (job_id, item_key), KEY job_status (job_id, status)
            ) {$c};",
            "CREATE TABLE {$p}executions (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                suggested_change_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
                approval_decision_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
                job_id BIGINT UNSIGNED NULL,
                idempotency_key VARCHAR(190) NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'queued',
                target_identity_hash CHAR(64) NULL,
                before_json LONGTEXT NULL, after_json LONGTEXT NULL,
                verification_json LONGTEXT NULL, error_json LONGTEXT NULL,
                created_by BIGINT UNSIGNED NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL, completed_at DATETIME NULL,
                PRIMARY KEY (id), UNIQUE KEY idempotency_key (idempotency_key),
                KEY site_status (site_id, status), KEY change_id (suggested_change_id)
            ) {$c};",
            "CREATE TABLE {$p}evidence (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NOT NULL,
                execution_id BIGINT UNSIGNED NULL,
                kind VARCHAR(64) NOT NULL,
                payload_json LONGTEXT NULL,
                storage_key VARCHAR(255) NULL,
                sha256 CHAR(64) NOT NULL,
                metadata_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY execution_id (execution_id), KEY site_created (site_id, created_at)
            ) {$c};",
            "CREATE TABLE {$p}receipts (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                execution_id BIGINT UNSIGNED NOT NULL,
                site_id BIGINT UNSIGNED NOT NULL,
                status VARCHAR(32) NOT NULL,
                receipt_uuid CHAR(36) NOT NULL,
                before_hash CHAR(64) NOT NULL, after_hash CHAR(64) NULL, evidence_hash CHAR(64) NULL,
                summary_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), UNIQUE KEY receipt_uuid (receipt_uuid), KEY execution_id (execution_id)
            ) {$c};",
            "CREATE TABLE {$p}ai_provider_config (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                provider_key VARCHAR(64) NOT NULL,
                label VARCHAR(190) NOT NULL,
                status VARCHAR(32) NOT NULL DEFAULT 'unconfigured',
                model VARCHAR(190) NULL,
                endpoint VARCHAR(2048) NULL,
                credential_ref VARCHAR(190) NULL,
                config_json LONGTEXT NULL,
                updated_by BIGINT UNSIGNED NOT NULL,
                updated_at DATETIME NOT NULL,
                PRIMARY KEY (id), UNIQUE KEY provider_key (provider_key), KEY status (status)
            ) {$c};",
            "CREATE TABLE {$p}ai_usage (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                provider_key VARCHAR(64) NOT NULL,
                site_id BIGINT UNSIGNED NULL,
                job_id BIGINT UNSIGNED NULL,
                operation VARCHAR(64) NOT NULL,
                input_units BIGINT UNSIGNED NOT NULL DEFAULT 0,
                output_units BIGINT UNSIGNED NOT NULL DEFAULT 0,
                duration_ms BIGINT UNSIGNED NOT NULL DEFAULT 0,
                status VARCHAR(32) NOT NULL,
                metadata_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY provider_created (provider_key, created_at), KEY job_id (job_id)
            ) {$c};",
            "CREATE TABLE {$p}activity_log (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                site_id BIGINT UNSIGNED NULL,
                actor_user_id BIGINT UNSIGNED NULL,
                category VARCHAR(64) NOT NULL,
                action VARCHAR(190) NOT NULL,
                object_type VARCHAR(64) NULL,
                object_id VARCHAR(190) NULL,
                duration_ms BIGINT UNSIGNED NULL,
                context_json LONGTEXT NULL,
                created_at DATETIME NOT NULL,
                PRIMARY KEY (id), KEY site_created (site_id, created_at), KEY category_created (category, created_at)
            ) {$c};",
        ];
        foreach ($sql as $statement) { dbDelta($statement); }
        self::migrate_legacy_rows($p);
        update_option('aiwm_web_schema_version', self::SCHEMA_VERSION, false);
        self::invalidate_dashboard();
    }

    private static function migrate_legacy_rows(string $p): void
    {
        global $wpdb;
        $audits_exists = $wpdb->get_var($wpdb->prepare('SHOW TABLES LIKE %s', $p . 'audits')) === $p . 'audits';
        if ($audits_exists) {
            $wpdb->query("INSERT IGNORE INTO {$p}seo_audits (id,site_id,status,score,findings_count,started_at,completed_at,created_at) SELECT id,site_id,status,score,findings_count,started_at,completed_at,created_at FROM {$p}audits");
        }
        $recommendations_exists = $wpdb->get_var($wpdb->prepare('SHOW TABLES LIKE %s', $p . 'recommendations')) === $p . 'recommendations';
        if ($recommendations_exists) {
            $wpdb->query("INSERT IGNORE INTO {$p}suggested_changes (id,site_id,audit_id,object_type,object_id,risk,status,before_json,proposed_json,version_hash,created_at,updated_at) SELECT id,site_id,audit_id,object_type,object_id,risk,status,COALESCE(before_json,'{}'),proposed_json,SHA2(CONCAT(COALESCE(before_json,''),'|',proposed_json),256),created_at,updated_at FROM {$p}recommendations");
        }
    }

    public static function now(): string { return current_time('mysql', true); }

    public static function json($value): string
    {
        return wp_json_encode($value, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE) ?: '{}';
    }

    public static function hash_payload($value): string
    {
        return hash('sha256', is_string($value) ? $value : self::json($value));
    }

    public static function decode(?string $value, $default = [])
    {
        if (!$value) { return $default; }
        $decoded = json_decode($value, true);
        return json_last_error() === JSON_ERROR_NONE ? $decoded : $default;
    }

    public static function invalidate_dashboard(): void
    {
        wp_cache_delete('dashboard', self::CACHE_GROUP);
        delete_transient('aiwm_web_dashboard');
    }

    public static function dashboard(): array
    {
        $cached = wp_cache_get('dashboard', self::CACHE_GROUP);
        if (is_array($cached)) { return $cached; }
        $cached = get_transient('aiwm_web_dashboard');
        if (is_array($cached)) { wp_cache_set('dashboard', $cached, self::CACHE_GROUP, 30); return $cached; }
        global $wpdb;
        $sites = self::table('sites'); $changes = self::table('suggested_changes'); $jobs = self::table('jobs'); $executions = self::table('executions');
        $result = [
            'counts' => [
                'sites' => (int) $wpdb->get_var("SELECT COUNT(*) FROM {$sites}"),
                'verifiedSites' => (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$sites} WHERE status=%s", 'verified')),
                'pendingRecommendations' => (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$changes} WHERE status=%s", 'pending')),
                'runningJobs' => (int) $wpdb->get_var("SELECT COUNT(*) FROM {$jobs} WHERE status IN ('queued','running','retry')"),
                'failedExecutions' => (int) $wpdb->get_var($wpdb->prepare("SELECT COUNT(*) FROM {$executions} WHERE status=%s", 'failed')),
            ],
            'generatedAt' => gmdate('c'),
        ];
        set_transient('aiwm_web_dashboard', $result, 30);
        wp_cache_set('dashboard', $result, self::CACHE_GROUP, 30);
        return $result;
    }

    public static function audit(string $category, string $action, ?int $site_id = null, ?string $object_type = null, ?string $object_id = null, array $context = [], ?int $duration_ms = null): void
    {
        global $wpdb;
        $wpdb->insert(self::table('activity_log'), [
            'site_id' => $site_id ?: null,
            'actor_user_id' => get_current_user_id() ?: null,
            'category' => sanitize_key($category),
            'action' => sanitize_text_field($action),
            'object_type' => $object_type ? sanitize_key($object_type) : null,
            'object_id' => $object_id ? sanitize_text_field($object_id) : null,
            'duration_ms' => $duration_ms,
            'context_json' => self::json($context),
            'created_at' => self::now(),
        ], ['%d','%d','%s','%s','%s','%s','%d','%s','%s']);
    }

    public static function add_evidence(int $site_id, ?int $execution_id, string $kind, $payload, array $metadata = []): int
    {
        global $wpdb;
        $json = self::json($payload);
        $wpdb->insert(self::table('evidence'), [
            'site_id' => $site_id,
            'execution_id' => $execution_id,
            'kind' => sanitize_key($kind),
            'payload_json' => $json,
            'storage_key' => null,
            'sha256' => hash('sha256', $json),
            'metadata_json' => self::json($metadata),
            'created_at' => self::now(),
        ], ['%d','%d','%s','%s','%s','%s','%s','%s']);
        return (int) $wpdb->insert_id;
    }
}
