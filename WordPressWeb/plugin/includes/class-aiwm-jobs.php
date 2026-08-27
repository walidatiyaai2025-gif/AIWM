<?php
if (!defined('ABSPATH')) { exit; }

final class AIWM_Web_Jobs
{
    private const HOOK = 'aiwm_web_process_job';
    private const SLICE_LIMIT = 25;
    private const SLICE_SECONDS = 8.0;

    public static function boot(): void
    {
        add_action(self::HOOK, [self::class, 'run'], 10, 1);
        add_filter('cron_schedules', [self::class, 'cron_schedules']);
    }

    public static function cron_schedules(array $schedules): array
    {
        $schedules['aiwm_minute'] = ['interval' => 60, 'display' => __('AIWM each minute', 'aiwm-web')];
        return $schedules;
    }

    public static function enqueue(string $type, ?int $site_id, array $payload, string $idempotency_key, int $total = 0): int
    {
        global $wpdb;
        $table = AIWM_Web_Store::table('jobs');
        $existing = (int) $wpdb->get_var($wpdb->prepare("SELECT id FROM {$table} WHERE idempotency_key=%s", $idempotency_key));
        if ($existing) { return $existing; }
        $wpdb->insert($table, [
            'site_id' => $site_id ?: null,
            'type' => sanitize_key($type),
            'status' => 'queued',
            'idempotency_key' => sanitize_text_field($idempotency_key),
            'progress_current' => 0,
            'progress_total' => max(0, $total),
            'cursor_json' => AIWM_Web_Store::json([]),
            'payload_json' => AIWM_Web_Store::json($payload),
            'attempts' => 0,
            'max_attempts' => 5,
            'created_at' => AIWM_Web_Store::now(),
        ]);
        if (!$wpdb->insert_id) {
            $existing = (int) $wpdb->get_var($wpdb->prepare("SELECT id FROM {$table} WHERE idempotency_key=%s", $idempotency_key));
            if ($existing) { return $existing; }
            throw new RuntimeException('Unable to enqueue AIWM job.');
        }
        $id = (int) $wpdb->insert_id;
        self::schedule($id, 0);
        AIWM_Web_Store::invalidate_dashboard();
        AIWM_Web_Store::audit('job', 'enqueued', $site_id, 'job', (string) $id, ['type' => $type]);
        return $id;
    }

    public static function cancel(int $job_id): bool
    {
        global $wpdb;
        $table = AIWM_Web_Store::table('jobs');
        $updated = $wpdb->query($wpdb->prepare(
            "UPDATE {$table} SET cancel_requested_at=%s WHERE id=%d AND status IN ('queued','running','retry')",
            AIWM_Web_Store::now(), $job_id
        ));
        if ($updated) { self::schedule($job_id, 0); }
        return (bool) $updated;
    }

    public static function schedule(int $job_id, int $delay_seconds = 1): void
    {
        $when = time() + max(0, $delay_seconds);
        if (function_exists('as_schedule_single_action')) {
            as_schedule_single_action($when, self::HOOK, [$job_id], 'aiwm-web', true);
            return;
        }
        if (!wp_next_scheduled(self::HOOK, [$job_id])) {
            wp_schedule_single_event($when, self::HOOK, [$job_id]);
        }
    }

    public static function run(int $job_id): void
    {
        global $wpdb;
        $table = AIWM_Web_Store::table('jobs');
        $job = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$table} WHERE id=%d", $job_id), ARRAY_A);
        if (!$job || in_array($job['status'], ['completed','failed','cancelled','partially_failed'], true)) { return; }
        if (!empty($job['cancel_requested_at'])) {
            if ($job['type'] === 'execution') { self::finalize_execution_terminal($job, 'cancelled', ['code' => 'cancelled', 'message' => 'Execution cancelled before mutation.']); }
            self::finish($job_id, 'cancelled', null);
            return;
        }
        $token = wp_generate_uuid4();
        $claimed = $wpdb->query($wpdb->prepare(
            "UPDATE {$table} SET status='running', lock_token=%s, locked_at=%s, last_heartbeat_at=%s, started_at=COALESCE(started_at,%s), attempts=attempts+1 WHERE id=%d AND status IN ('queued','retry','running') AND (locked_at IS NULL OR locked_at < DATE_SUB(UTC_TIMESTAMP(), INTERVAL 2 MINUTE))",
            $token, AIWM_Web_Store::now(), AIWM_Web_Store::now(), AIWM_Web_Store::now(), $job_id
        ));
        if (!$claimed) { return; }
        $job = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$table} WHERE id=%d", $job_id), ARRAY_A);
        $started = microtime(true);
        try {
            $payload = AIWM_Web_Store::decode($job['payload_json'], []);
            $cursor = AIWM_Web_Store::decode($job['cursor_json'], []);
            if ($job['type'] === 'execution') {
                $result = self::process_execution($job, $payload);
            } else {
                $result = apply_filters('aiwm_web_process_job_slice', null, $job, $payload, $cursor, self::SLICE_LIMIT, self::SLICE_SECONDS);
                if (!is_array($result)) {
                    throw new RuntimeException('No runtime adapter is registered for job type: ' . $job['type']);
                }
            }
            self::apply_result($job, $result);
        } catch (Throwable $e) {
            self::retry_or_fail($job, $e);
        } finally {
            $duration = (int) round((microtime(true) - $started) * 1000);
            AIWM_Web_Store::audit('performance', 'job_slice', isset($job['site_id']) ? (int) $job['site_id'] : null, 'job', (string) $job_id, ['type' => $job['type']], $duration);
        }
    }

    private static function process_execution(array $job, array $payload): array
    {
        global $wpdb;
        $execution_id = (int) ($payload['execution_id'] ?? 0);
        $executions = AIWM_Web_Store::table('executions');
        $changes = AIWM_Web_Store::table('suggested_changes');
        $approvals = AIWM_Web_Store::table('approval_decisions');
        $sites = AIWM_Web_Store::table('sites');
        $execution = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$executions} WHERE id=%d", $execution_id), ARRAY_A);
        if (!$execution) { throw new RuntimeException('Execution record not found.'); }
        $site = $wpdb->get_row($wpdb->prepare("SELECT id,name,base_url,status,auth_type,credential_ref,identity_fingerprint FROM {$sites} WHERE id=%d", $execution['site_id']), ARRAY_A);
        $change = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$changes} WHERE id=%d", $execution['suggested_change_id']), ARRAY_A);
        $approval = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$approvals} WHERE id=%d", $execution['approval_decision_id']), ARRAY_A);
        if (!$site || $site['status'] !== 'verified' || empty($site['identity_fingerprint'])) { throw new RuntimeException('Target site identity is not verified.'); }
        if (!hash_equals((string) $execution['target_identity_hash'], (string) $site['identity_fingerprint'])) { throw new RuntimeException('Target site identity changed after approval.'); }
        if (!$change || !$approval || $approval['decision'] !== 'approved' || !hash_equals((string) $approval['decision_version_hash'], (string) $change['version_hash'])) { throw new RuntimeException('Approval is missing, stale, or does not match the proposed change.'); }
        if (empty($execution['before_json'])) { throw new RuntimeException('Before-state evidence is required.'); }

        $identity = apply_filters('aiwm_web_verify_site_identity', null, $site, $execution);
        if (!is_array($identity) || empty($identity['fingerprint'])) { throw new RuntimeException('No live target identity verifier is registered for this execution.'); }
        if (!hash_equals((string) $site['identity_fingerprint'], (string) $identity['fingerprint'])) { throw new RuntimeException('Live target identity does not match the approved site identity.'); }
        AIWM_Web_Store::add_evidence((int) $execution['site_id'], $execution_id, 'identity_recheck', [
            'fingerprint' => (string) $identity['fingerprint'],
            'verified_at' => gmdate('c'),
        ]);

        $adapter = apply_filters('aiwm_web_execute_change', null, $execution, $site, $change, $approval);
        if (!is_array($adapter)) { throw new RuntimeException('No remote mutation adapter is registered for this execution.'); }
        $status = sanitize_key((string) ($adapter['status'] ?? 'failed'));
        if (!in_array($status, ['completed','failed','partially_failed','cancelled'], true)) { $status = 'failed'; }
        $after = $adapter['after'] ?? null;
        $verification = $adapter['verification'] ?? null;
        $error = $adapter['error'] ?? null;
        $wpdb->update($executions, [
            'status' => $status,
            'after_json' => $after !== null ? AIWM_Web_Store::json($after) : null,
            'verification_json' => $verification !== null ? AIWM_Web_Store::json($verification) : null,
            'error_json' => $error !== null ? AIWM_Web_Store::json($error) : null,
            'completed_at' => AIWM_Web_Store::now(),
        ], ['id' => $execution_id]);
        $evidence_payload = ['after' => $after, 'verification' => $verification, 'status' => $status];
        $evidence_id = AIWM_Web_Store::add_evidence((int) $execution['site_id'], $execution_id, 'execution_result', $evidence_payload);
        $before_hash = hash('sha256', (string) $execution['before_json']);
        $after_hash = $after !== null ? AIWM_Web_Store::hash_payload($after) : null;
        $evidence_hash = AIWM_Web_Store::hash_payload($evidence_payload);
        $wpdb->insert(AIWM_Web_Store::table('receipts'), [
            'execution_id' => $execution_id, 'site_id' => (int) $execution['site_id'], 'status' => $status,
            'receipt_uuid' => wp_generate_uuid4(), 'before_hash' => $before_hash, 'after_hash' => $after_hash,
            'evidence_hash' => $evidence_hash, 'summary_json' => AIWM_Web_Store::json(['evidence_id' => $evidence_id]),
            'created_at' => AIWM_Web_Store::now(),
        ]);
        AIWM_Web_Store::invalidate_dashboard();
        return ['done' => true, 'status' => $status === 'partially_failed' ? 'partially_failed' : ($status === 'cancelled' ? 'cancelled' : ($status === 'completed' ? 'completed' : 'failed')), 'current' => 1, 'total' => 1, 'error' => $error];
    }

    private static function apply_result(array $job, array $result): void
    {
        global $wpdb;
        $table = AIWM_Web_Store::table('jobs');
        $job_id = (int) $job['id'];
        $current = max(0, (int) ($result['current'] ?? $job['progress_current']));
        $total = max($current, (int) ($result['total'] ?? $job['progress_total']));
        $cursor = $result['cursor'] ?? AIWM_Web_Store::decode($job['cursor_json'], []);
        $done = !empty($result['done']);
        $terminal = sanitize_key((string) ($result['status'] ?? ($done ? 'completed' : 'queued')));
        if ($done) {
            if (!in_array($terminal, ['completed','failed','partially_failed','cancelled'], true)) { $terminal = 'completed'; }
            self::finish($job_id, $terminal, $result['error'] ?? null, $current, $total, $cursor);
            return;
        }
        $wpdb->update($table, [
            'status' => 'queued', 'progress_current' => $current, 'progress_total' => $total,
            'cursor_json' => AIWM_Web_Store::json($cursor), 'lock_token' => null, 'locked_at' => null,
            'last_heartbeat_at' => AIWM_Web_Store::now(),
        ], ['id' => $job_id]);
        self::schedule($job_id, 2);
    }

    private static function retry_or_fail(array $job, Throwable $e): void
    {
        global $wpdb;
        $attempts = (int) $job['attempts'];
        $max = max(1, (int) $job['max_attempts']);
        if ($attempts >= $max) {
            if ($job['type'] === 'execution') { self::finalize_execution_terminal($job, 'failed', ['code' => 'job_failed', 'message' => $e->getMessage()]); }
            self::finish((int) $job['id'], 'failed', ['code' => 'job_failed', 'message' => $e->getMessage()]);
            return;
        }
        $delay = min(300, (int) pow(2, min($attempts, 8)) * 5);
        $wpdb->update(AIWM_Web_Store::table('jobs'), [
            'status' => 'retry', 'error_json' => AIWM_Web_Store::json(['code' => 'retry', 'message' => $e->getMessage()]),
            'next_attempt_at' => gmdate('Y-m-d H:i:s', time() + $delay), 'lock_token' => null, 'locked_at' => null,
        ], ['id' => (int) $job['id']]);
        self::schedule((int) $job['id'], $delay);
    }

    private static function finalize_execution_terminal(array $job, string $status, array $error): void
    {
        global $wpdb;
        $payload = AIWM_Web_Store::decode($job['payload_json'] ?? null, []);
        $execution_id = (int) ($payload['execution_id'] ?? 0);
        if ($execution_id < 1) { return; }
        $table = AIWM_Web_Store::table('executions');
        $execution = $wpdb->get_row($wpdb->prepare("SELECT * FROM {$table} WHERE id=%d", $execution_id), ARRAY_A);
        if (!$execution || in_array($execution['status'], ['completed','failed','partially_failed','cancelled'], true)) { return; }
        $wpdb->update($table, [
            'status' => $status,
            'error_json' => AIWM_Web_Store::json($error),
            'completed_at' => AIWM_Web_Store::now(),
        ], ['id' => $execution_id]);
        $evidence_payload = ['status' => $status, 'error' => $error];
        AIWM_Web_Store::add_evidence((int) $execution['site_id'], $execution_id, 'execution_terminal', $evidence_payload);
        $wpdb->insert(AIWM_Web_Store::table('receipts'), [
            'execution_id' => $execution_id,
            'site_id' => (int) $execution['site_id'],
            'status' => $status,
            'receipt_uuid' => wp_generate_uuid4(),
            'before_hash' => hash('sha256', (string) ($execution['before_json'] ?? '')),
            'after_hash' => null,
            'evidence_hash' => AIWM_Web_Store::hash_payload($evidence_payload),
            'summary_json' => AIWM_Web_Store::json(['error' => $error]),
            'created_at' => AIWM_Web_Store::now(),
        ]);
        AIWM_Web_Store::invalidate_dashboard();
    }

    private static function finish(int $job_id, string $status, $error = null, ?int $current = null, ?int $total = null, $cursor = null): void
    {
        global $wpdb;
        $data = [
            'status' => $status, 'finished_at' => AIWM_Web_Store::now(), 'lock_token' => null, 'locked_at' => null,
            'last_heartbeat_at' => AIWM_Web_Store::now(), 'error_json' => $error !== null ? AIWM_Web_Store::json($error) : null,
        ];
        if ($current !== null) { $data['progress_current'] = $current; }
        if ($total !== null) { $data['progress_total'] = $total; }
        if ($cursor !== null) { $data['cursor_json'] = AIWM_Web_Store::json($cursor); }
        $wpdb->update(AIWM_Web_Store::table('jobs'), $data, ['id' => $job_id]);
        AIWM_Web_Store::invalidate_dashboard();
        AIWM_Web_Store::audit('job', 'finished', null, 'job', (string) $job_id, ['status' => $status]);
    }
}
