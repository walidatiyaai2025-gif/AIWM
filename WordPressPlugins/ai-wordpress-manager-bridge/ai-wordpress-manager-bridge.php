<?php
/**
 * Plugin Name: AI WordPress Manager Bridge
 * Description: Protected capability and execution bridge for AI WordPress Website Manager.
 * Version: 1.3.0
 * Author: AI WordPress Manager
 * Requires at least: 6.0
 * Requires PHP: 7.4
 */

if (!defined('ABSPATH')) {
    exit;
}

define('AIWP_MANAGER_BRIDGE_VERSION', '1.3.0');
define('AIWP_MANAGER_CSS_START', '/* AIWP-MANAGED-START */');
define('AIWP_MANAGER_CSS_END', '/* AIWP-MANAGED-END */');
define('AIWP_MANAGER_HISTORY_OPTION', 'aiwp_manager_visual_css_history');

function aiwp_manager_can_execute_visual_css(): bool {
    return is_user_logged_in() && current_user_can('edit_theme_options');
}

function aiwp_manager_validate_selector(string $selector): bool {
    if ($selector === '' || strlen($selector) > 1000) {
        return false;
    }

    return !preg_match('/[{}<>\\x00-\\x1F]/', $selector);
}

function aiwp_manager_validate_css(string $css): bool {
    if ($css === '' || strlen($css) > 20000) {
        return false;
    }

    if (preg_match('/[{}]/', $css)) {
        return false;
    }

    return !preg_match('/@import|expression\s*\(|javascript\s*:|<\/style/i', $css);
}

function aiwp_manager_replace_managed_css(string $full_css, string $managed_css): string {
    $block = AIWP_MANAGER_CSS_START . "\n" . trim($managed_css) . "\n" . AIWP_MANAGER_CSS_END;
    $pattern = '/' . preg_quote(AIWP_MANAGER_CSS_START, '/') . '.*?' . preg_quote(AIWP_MANAGER_CSS_END, '/') . '/s';

    if (preg_match($pattern, $full_css)) {
        return (string) preg_replace($pattern, $block, $full_css, 1);
    }

    return rtrim($full_css) . "\n\n" . $block . "\n";
}

function aiwp_manager_get_managed_css(string $full_css): string {
    $pattern = '/' . preg_quote(AIWP_MANAGER_CSS_START, '/') . '\s*(.*?)\s*' . preg_quote(AIWP_MANAGER_CSS_END, '/') . '/s';
    if (!preg_match($pattern, $full_css, $matches)) {
        return '';
    }

    return isset($matches[1]) ? trim((string) $matches[1]) : '';
}

function aiwp_manager_get_history(): array {
    $history = get_option(AIWP_MANAGER_HISTORY_OPTION, []);
    return is_array($history) ? array_values($history) : [];
}

function aiwp_manager_save_history(array $history): void {
    $history = array_slice(array_values($history), 0, 100);
    update_option(AIWP_MANAGER_HISTORY_OPTION, $history, false);
}

function aiwp_manager_add_history(array $entry): void {
    $history = aiwp_manager_get_history();
    array_unshift($history, $entry);
    aiwp_manager_save_history($history);
}

function aiwp_manager_mark_history_rolled_back(string $change_id): void {
    $history = aiwp_manager_get_history();
    foreach ($history as &$entry) {
        if (($entry['change_id'] ?? '') === $change_id) {
            $entry['status'] = 'rolled_back';
            $entry['rolled_back_at_utc'] = gmdate('c');
            break;
        }
    }
    unset($entry);
    aiwp_manager_save_history($history);
}

function aiwp_manager_public_history_item(array $entry): array {
    return [
        'change_id' => (string) ($entry['change_id'] ?? ''),
        'page_url' => (string) ($entry['page_url'] ?? ''),
        'selector' => (string) ($entry['selector'] ?? ''),
        'css' => (string) ($entry['css'] ?? ''),
        'status' => (string) ($entry['status'] ?? 'active'),
        'active_stylesheet' => (string) ($entry['stylesheet'] ?? ''),
        'executed_at_utc' => (string) ($entry['executed_at_utc'] ?? ''),
        'rolled_back_at_utc' => (string) ($entry['rolled_back_at_utc'] ?? ''),
        'executed_by' => (string) ($entry['executed_by'] ?? ''),
    ];
}

add_action('rest_api_init', static function (): void {
    register_rest_route('aiwp-manager/v1', '/health', [
        'methods' => 'GET',
        'callback' => static function (): WP_REST_Response {
            return new WP_REST_Response([
                'ok' => true,
                'plugin_version' => AIWP_MANAGER_BRIDGE_VERSION,
                'site_url' => get_site_url(),
                'wordpress_version' => get_bloginfo('version'),
                'php_version' => PHP_VERSION,
                'rest_available' => true,
                'can_edit_posts' => current_user_can('edit_posts'),
                'can_upload_files' => current_user_can('upload_files'),
                'can_edit_theme_options' => current_user_can('edit_theme_options'),
                'active_theme' => wp_get_theme()->get('Name'),
                'active_stylesheet' => get_stylesheet(),
                'seo_plugins' => [
                    'yoast' => defined('WPSEO_VERSION'),
                    'rank_math' => defined('RANK_MATH_VERSION'),
                ],
                'page_builders' => [
                    'elementor' => defined('ELEMENTOR_VERSION'),
                    'divi' => defined('ET_CORE_VERSION'),
                ],
            ], 200);
        },
        'permission_callback' => static function (): bool {
            return is_user_logged_in() && current_user_can('edit_posts');
        },
    ]);

    register_rest_route('aiwp-manager/v1', '/visual-css', [
        [
            'methods' => 'GET',
            'callback' => static function (): WP_REST_Response {
                return new WP_REST_Response([
                    'ok' => true,
                    'plugin_version' => AIWP_MANAGER_BRIDGE_VERSION,
                    'can_edit_theme_options' => current_user_can('edit_theme_options'),
                    'active_stylesheet' => get_stylesheet(),
                    'message' => 'Visual CSS bridge is available.',
                ], 200);
            },
            'permission_callback' => 'aiwp_manager_can_execute_visual_css',
        ],
        [
            'methods' => 'POST',
            'callback' => static function (WP_REST_Request $request) {
                $selector = trim((string) $request->get_param('selector'));
                $css = trim((string) $request->get_param('css'));
                $page_url = esc_url_raw((string) $request->get_param('page_url'));

                if (!aiwp_manager_validate_selector($selector)) {
                    return new WP_Error('aiwp_invalid_selector', 'The CSS selector is empty or contains unsafe characters.', ['status' => 400]);
                }

                if (!aiwp_manager_validate_css($css)) {
                    return new WP_Error('aiwp_invalid_css', 'The CSS declarations are empty or contain unsupported/unsafe syntax.', ['status' => 400]);
                }

                $stylesheet = get_stylesheet();
                $previous_full_css = wp_get_custom_css($stylesheet);
                $previous_managed_css = aiwp_manager_get_managed_css($previous_full_css);
                $change_id = wp_generate_uuid4();
                $new_rule = sprintf("/* change:%s */\n%s {\n%s\n}", $change_id, $selector, $css);
                $new_managed_css = trim($previous_managed_css . "\n\n" . $new_rule);
                $updated_full_css = aiwp_manager_replace_managed_css($previous_full_css, $new_managed_css);

                $rollback_token = wp_generate_password(48, false, false);
                set_transient('aiwp_css_rollback_' . hash('sha256', $rollback_token), [
                    'change_id' => $change_id,
                    'stylesheet' => $stylesheet,
                    'previous_full_css' => $previous_full_css,
                    'selector' => $selector,
                    'css' => $css,
                    'created_at' => time(),
                ], 7 * DAY_IN_SECONDS);

                $result = wp_update_custom_css_post($updated_full_css, ['stylesheet' => $stylesheet]);
                if (is_wp_error($result)) {
                    return $result;
                }

                clean_post_cache($result->ID);
                $user = wp_get_current_user();
                aiwp_manager_add_history([
                    'change_id' => $change_id,
                    'page_url' => $page_url,
                    'selector' => $selector,
                    'css' => $css,
                    'status' => 'active',
                    'stylesheet' => $stylesheet,
                    'previous_full_css' => $previous_full_css,
                    'executed_at_utc' => gmdate('c'),
                    'rolled_back_at_utc' => '',
                    'executed_by' => $user instanceof WP_User ? $user->user_login : '',
                ]);

                return new WP_REST_Response([
                    'ok' => true,
                    'change_id' => $change_id,
                    'message' => 'Visual CSS was written to the active theme Custom CSS revision.',
                    'selector' => $selector,
                    'css' => $css,
                    'previous_managed_css' => $previous_managed_css,
                    'applied_managed_css' => $new_managed_css,
                    'rollback_token' => $rollback_token,
                    'custom_css_post_id' => $result->ID,
                    'executed_at_utc' => gmdate('c'),
                ], 200);
            },
            'permission_callback' => 'aiwp_manager_can_execute_visual_css',
        ],
    ]);

    register_rest_route('aiwp-manager/v1', '/visual-css/validate', [
        'methods' => 'POST',
        'callback' => static function (WP_REST_Request $request) {
            $selector = trim((string) $request->get_param('selector'));
            $css = trim((string) $request->get_param('css'));
            $page_url = esc_url_raw((string) $request->get_param('page_url'));

            if (!aiwp_manager_validate_selector($selector)) {
                return new WP_Error('aiwp_invalid_selector', 'The CSS selector is empty or contains unsafe characters.', ['status' => 400]);
            }

            if (!aiwp_manager_validate_css($css)) {
                return new WP_Error('aiwp_invalid_css', 'The CSS declarations are empty or contain unsupported/unsafe syntax.', ['status' => 400]);
            }

            $stylesheet = get_stylesheet();
            $managed_css = aiwp_manager_get_managed_css(wp_get_custom_css($stylesheet));
            $rule_count = preg_match_all('/\/\* change:[^*]+\*\//', $managed_css, $matches);

            return new WP_REST_Response([
                'ok' => true,
                'valid' => true,
                'message' => 'The selector and CSS declarations passed Bridge validation. No WordPress data was changed.',
                'page_url' => $page_url,
                'selector' => $selector,
                'css' => $css,
                'active_stylesheet' => $stylesheet,
                'managed_css_checksum' => hash('sha256', $managed_css),
                'managed_rule_count' => (int) $rule_count,
                'plugin_version' => AIWP_MANAGER_BRIDGE_VERSION,
                'validated_at_utc' => gmdate('c'),
            ], 200);
        },
        'permission_callback' => 'aiwp_manager_can_execute_visual_css',
    ]);

    register_rest_route('aiwp-manager/v1', '/visual-css/history', [
        'methods' => 'GET',
        'callback' => static function (): WP_REST_Response {
            $stylesheet = get_stylesheet();
            $managed_css = aiwp_manager_get_managed_css(wp_get_custom_css($stylesheet));
            $rule_count = preg_match_all('/\/\* change:[^*]+\*\//', $managed_css, $matches);
            $items = array_map('aiwp_manager_public_history_item', aiwp_manager_get_history());

            return new WP_REST_Response([
                'ok' => true,
                'plugin_version' => AIWP_MANAGER_BRIDGE_VERSION,
                'active_stylesheet' => $stylesheet,
                'managed_rule_count' => (int) $rule_count,
                'managed_css_checksum' => hash('sha256', $managed_css),
                'items' => $items,
            ], 200);
        },
        'permission_callback' => 'aiwp_manager_can_execute_visual_css',
    ]);

    register_rest_route('aiwp-manager/v1', '/visual-css/history/rollback', [
        'methods' => 'POST',
        'callback' => static function (WP_REST_Request $request) {
            $change_id = sanitize_text_field((string) $request->get_param('change_id'));
            if ($change_id === '') {
                return new WP_Error('aiwp_missing_change_id', 'A change ID is required.', ['status' => 400]);
            }

            $history = aiwp_manager_get_history();
            $target = null;
            foreach ($history as $entry) {
                if (($entry['change_id'] ?? '') === $change_id) {
                    $target = $entry;
                    break;
                }
            }

            if (!is_array($target)) {
                return new WP_Error('aiwp_change_not_found', 'The selected managed CSS change was not found.', ['status' => 404]);
            }

            if (($target['status'] ?? '') === 'rolled_back') {
                return new WP_Error('aiwp_already_rolled_back', 'The selected change has already been rolled back.', ['status' => 409]);
            }

            $stylesheet = (string) ($target['stylesheet'] ?? get_stylesheet());
            $previous_full_css = (string) ($target['previous_full_css'] ?? '');
            $result = wp_update_custom_css_post($previous_full_css, ['stylesheet' => $stylesheet]);
            if (is_wp_error($result)) {
                return $result;
            }

            clean_post_cache($result->ID);
            aiwp_manager_mark_history_rolled_back($change_id);

            return new WP_REST_Response([
                'ok' => true,
                'change_id' => $change_id,
                'message' => 'The selected managed CSS change was rolled back from Bridge history.',
                'selector' => (string) ($target['selector'] ?? ''),
                'css' => (string) ($target['css'] ?? ''),
                'previous_managed_css' => aiwp_manager_get_managed_css(wp_get_custom_css($stylesheet)),
                'applied_managed_css' => aiwp_manager_get_managed_css($previous_full_css),
                'rollback_token' => '',
                'custom_css_post_id' => $result->ID,
                'executed_at_utc' => gmdate('c'),
            ], 200);
        },
        'permission_callback' => 'aiwp_manager_can_execute_visual_css',
    ]);

    register_rest_route('aiwp-manager/v1', '/visual-css/rollback', [
        'methods' => 'POST',
        'callback' => static function (WP_REST_Request $request) {
            $change_id = sanitize_text_field((string) $request->get_param('change_id'));
            $rollback_token = sanitize_text_field((string) $request->get_param('rollback_token'));
            $transient_key = 'aiwp_css_rollback_' . hash('sha256', $rollback_token);
            $rollback = get_transient($transient_key);

            if (!is_array($rollback) || !hash_equals((string) ($rollback['change_id'] ?? ''), $change_id)) {
                return new WP_Error('aiwp_invalid_rollback', 'The rollback token is invalid or expired.', ['status' => 400]);
            }

            $stylesheet = (string) ($rollback['stylesheet'] ?? get_stylesheet());
            $previous_full_css = (string) ($rollback['previous_full_css'] ?? '');
            $result = wp_update_custom_css_post($previous_full_css, ['stylesheet' => $stylesheet]);
            if (is_wp_error($result)) {
                return $result;
            }

            delete_transient($transient_key);
            clean_post_cache($result->ID);
            aiwp_manager_mark_history_rolled_back($change_id);

            return new WP_REST_Response([
                'ok' => true,
                'change_id' => $change_id,
                'message' => 'The previous Custom CSS revision was restored.',
                'selector' => (string) ($rollback['selector'] ?? ''),
                'css' => (string) ($rollback['css'] ?? ''),
                'previous_managed_css' => aiwp_manager_get_managed_css(wp_get_custom_css($stylesheet)),
                'applied_managed_css' => aiwp_manager_get_managed_css($previous_full_css),
                'rollback_token' => '',
                'custom_css_post_id' => $result->ID,
                'executed_at_utc' => gmdate('c'),
            ], 200);
        },
        'permission_callback' => 'aiwp_manager_can_execute_visual_css',
    ]);
});
