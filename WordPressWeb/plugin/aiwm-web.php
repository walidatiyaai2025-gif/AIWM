<?php
/**
 * Plugin Name: AI WordPress Manager Web Edition
 * Description: WordPress-hosted Web Edition of AI WordPress Manager.
 * Version: 0.2.0-dev
 * Requires at least: 6.5
 * Requires PHP: 8.1
 * Author: AIWM
 */

if (!defined('ABSPATH')) { exit; }

require_once __DIR__ . '/includes/class-aiwm-security.php';
require_once __DIR__ . '/includes/class-aiwm-store.php';
require_once __DIR__ . '/includes/class-aiwm-jobs.php';
require_once __DIR__ . '/includes/class-aiwm-rest.php';

final class AIWM_Web_Edition
{
    public const VERSION = '0.2.0-dev';
    private const CAPABILITY = 'manage_aiwm';

    public static function boot(): void
    {
        register_activation_hook(__FILE__, [self::class, 'activate']);
        add_action('plugins_loaded', [AIWM_Web_Store::class, 'maybe_migrate'], 5);
        add_action('admin_menu', [self::class, 'register_admin_menu']);
        add_action('admin_enqueue_scripts', [self::class, 'enqueue_admin_assets']);
        AIWM_Web_Jobs::boot();
        AIWM_Web_REST::boot();
    }

    public static function activate(): void
    {
        AIWM_Web_Store::install_schema();
        $admin = get_role('administrator');
        if ($admin && !$admin->has_cap(self::CAPABILITY)) { $admin->add_cap(self::CAPABILITY); }
        update_option('aiwm_web_version', self::VERSION, false);
    }

    public static function register_admin_menu(): void
    {
        add_menu_page(__('AI WordPress Manager', 'aiwm-web'), __('AI WordPress Manager', 'aiwm-web'), self::CAPABILITY, 'aiwm-web', [self::class, 'render_admin_app'], 'dashicons-admin-site-alt3', 3);
    }

    public static function render_admin_app(): void
    {
        if (!current_user_can(self::CAPABILITY)) { wp_die(esc_html__('You do not have permission to access AI WordPress Manager.', 'aiwm-web')); }
        echo '<div class="wrap aiwm-web-host"><div id="aiwm-web-root" data-status="booting"><div class="aiwm-web-boot">';
        echo '<h1>' . esc_html__('AI WordPress Manager', 'aiwm-web') . '</h1><p>' . esc_html__('Loading Web Edition…', 'aiwm-web') . '</p>';
        echo '</div></div></div>';
    }

    public static function enqueue_admin_assets(string $hook): void
    {
        if ($hook !== 'toplevel_page_aiwm-web') { return; }
        $base = plugin_dir_url(__FILE__); $path = plugin_dir_path(__FILE__);
        if (file_exists($path . 'assets/admin.css')) { wp_enqueue_style('aiwm-web-admin', $base . 'assets/admin.css', [], self::VERSION); }
        if (file_exists($path . 'assets/admin.js')) { wp_enqueue_script('aiwm-web-admin', $base . 'assets/admin.js', ['wp-api-fetch'], self::VERSION, true); }
        wp_add_inline_script('wp-api-fetch', 'window.AIWM_WEB_BOOTSTRAP = ' . wp_json_encode([
            'restRoot' => esc_url_raw(rest_url('aiwm/v1/')), 'nonce' => wp_create_nonce('wp_rest'), 'locale' => determine_locale(), 'isRtl' => is_rtl(), 'version' => self::VERSION,
        ]) . ';', 'before');
    }
}

AIWM_Web_Edition::boot();
