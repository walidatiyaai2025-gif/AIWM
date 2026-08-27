<?php
if (!defined('ABSPATH')) { exit; }

final class AIWM_Web_Security
{
    private const OPTION_PREFIX = 'aiwm_web_secret_';

    public static function can_read(): bool
    {
        return is_user_logged_in() && current_user_can('manage_aiwm');
    }

    public static function can_mutate(WP_REST_Request $request)
    {
        if (!self::can_read()) {
            return new WP_Error('aiwm_forbidden', __('You do not have permission to modify AIWM.', 'aiwm-web'), ['status' => 403]);
        }
        $nonce = $request->get_header('X-WP-Nonce');
        if (!$nonce || !wp_verify_nonce($nonce, 'wp_rest')) {
            return new WP_Error('aiwm_invalid_nonce', __('A valid WordPress REST nonce is required.', 'aiwm-web'), ['status' => 403]);
        }
        return true;
    }

    public static function store_secret(string $scope, string $secret): string
    {
        if ($secret === '') {
            throw new InvalidArgumentException('Secret cannot be empty.');
        }
        $ref = sanitize_key($scope) . '_' . wp_generate_uuid4();
        $payload = self::encrypt($secret);
        update_option(self::OPTION_PREFIX . hash('sha256', $ref), $payload, false);
        return $ref;
    }

    public static function replace_secret(?string $ref, string $scope, string $secret): string
    {
        if ($ref) {
            self::delete_secret($ref);
        }
        return self::store_secret($scope, $secret);
    }

    public static function reveal_secret(string $ref): string
    {
        $payload = get_option(self::OPTION_PREFIX . hash('sha256', $ref), '');
        if (!is_string($payload) || $payload === '') {
            throw new RuntimeException('Credential reference is unavailable.');
        }
        return self::decrypt($payload);
    }

    public static function delete_secret(string $ref): void
    {
        delete_option(self::OPTION_PREFIX . hash('sha256', $ref));
    }

    private static function key(): string
    {
        return hash('sha256', wp_salt('auth') . '|' . wp_salt('secure_auth') . '|aiwm-web', true);
    }

    private static function encrypt(string $plaintext): string
    {
        if (!function_exists('openssl_encrypt')) {
            throw new RuntimeException('OpenSSL is required for AIWM credential encryption.');
        }
        $iv = random_bytes(12);
        $tag = '';
        $cipher = openssl_encrypt($plaintext, 'aes-256-gcm', self::key(), OPENSSL_RAW_DATA, $iv, $tag);
        if ($cipher === false) {
            throw new RuntimeException('Credential encryption failed.');
        }
        return base64_encode("A1" . $iv . $tag . $cipher);
    }

    private static function decrypt(string $payload): string
    {
        $raw = base64_decode($payload, true);
        if ($raw === false || strlen($raw) < 30 || substr($raw, 0, 2) !== 'A1') {
            throw new RuntimeException('Credential payload is invalid.');
        }
        $iv = substr($raw, 2, 12);
        $tag = substr($raw, 14, 16);
        $cipher = substr($raw, 30);
        $plain = openssl_decrypt($cipher, 'aes-256-gcm', self::key(), OPENSSL_RAW_DATA, $iv, $tag);
        if ($plain === false) {
            throw new RuntimeException('Credential decryption failed.');
        }
        return $plain;
    }
}
