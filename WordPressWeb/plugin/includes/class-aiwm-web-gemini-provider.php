<?php

if (!defined('ABSPATH')) {
    exit;
}

final class AIWM_Web_Gemini_Provider
{
    private const DEFAULT_MODEL = 'gemini-2.5-flash';

    public function generate(array $candidates)
    {
        $apiKey = $this->resolve_api_key();
        if ($apiKey === '') {
            return new WP_Error(
                'aiwm_ai_missing_key',
                __('Gemini API key is not configured on the server.', 'aiwm-web'),
                ['status' => 503, 'provider' => 'Gemini']
            );
        }

        if (empty($candidates)) {
            return new WP_Error(
                'aiwm_ai_no_candidates',
                __('No supported SEO findings are available for AI recommendation.', 'aiwm-web'),
                ['status' => 400, 'provider' => 'Gemini']
            );
        }

        $model = $this->resolve_model();
        $endpoint = sprintf(
            'https://generativelanguage.googleapis.com/v1beta/models/%s:generateContent',
            rawurlencode($model)
        );

        $allowed = [];
        foreach ($candidates as $candidate) {
            $key = self::candidate_key($candidate);
            if ($key !== '') {
                $allowed[$key] = $candidate;
            }
        }

        $prompt = $this->build_prompt(array_values($allowed));
        $payload = [
            'contents' => [[
                'parts' => [['text' => $prompt]],
            ]],
            'generationConfig' => [
                'temperature' => 0.2,
                'responseMimeType' => 'application/json',
                'maxOutputTokens' => min(4096, max(900, count($allowed) * 500)),
            ],
        ];

        $response = wp_safe_remote_post($endpoint, [
            'timeout' => 25,
            'redirection' => 2,
            'headers' => [
                'Accept' => 'application/json',
                'Content-Type' => 'application/json',
                'x-goog-api-key' => $apiKey,
            ],
            'body' => wp_json_encode($payload),
        ]);

        if (is_wp_error($response)) {
            $message = strtolower($response->get_error_message());
            $code = str_contains($message, 'timed out') || str_contains($message, 'timeout')
                ? 'aiwm_ai_timeout'
                : 'aiwm_ai_unavailable';
            $friendly = $code === 'aiwm_ai_timeout'
                ? __('Gemini request timed out.', 'aiwm-web')
                : __('Gemini is unavailable from this WordPress host.', 'aiwm-web');
            return new WP_Error($code, $friendly, ['status' => 503, 'provider' => 'Gemini']);
        }

        $status = (int) wp_remote_retrieve_response_code($response);
        $body = (string) wp_remote_retrieve_body($response);
        if ($status < 200 || $status >= 300) {
            return $this->provider_error($status, $body);
        }

        $decoded = json_decode($body, true);
        $text = $decoded['candidates'][0]['content']['parts'][0]['text'] ?? null;
        if (!is_string($text) || trim($text) === '') {
            return new WP_Error(
                'aiwm_ai_malformed_response',
                __('Gemini returned a malformed response.', 'aiwm-web'),
                ['status' => 502, 'provider' => 'Gemini']
            );
        }

        $text = preg_replace('/^```(?:json)?\s*|\s*```$/i', '', trim($text));
        $suggestions = json_decode((string) $text, true);
        if (!is_array($suggestions)) {
            return new WP_Error(
                'aiwm_ai_malformed_response',
                __('Gemini returned invalid recommendation JSON.', 'aiwm-web'),
                ['status' => 502, 'provider' => 'Gemini']
            );
        }

        $validated = [];
        foreach ($suggestions as $suggestion) {
            if (!is_array($suggestion)) {
                continue;
            }

            $candidate = [
                'object_type' => sanitize_key($suggestion['object_type'] ?? ''),
                'object_id' => (string) absint($suggestion['object_id'] ?? 0),
                'field' => sanitize_key($suggestion['field'] ?? ''),
            ];
            $key = self::candidate_key($candidate);
            if ($key === '' || !isset($allowed[$key])) {
                continue;
            }

            $field = $candidate['field'];
            $proposed = $suggestion['proposed'] ?? '';
            if (!is_string($proposed)) {
                continue;
            }
            $proposed = self::sanitize_proposed($field, $proposed);
            if ($proposed === '') {
                continue;
            }

            $validated[$key] = [
                'object_type' => $candidate['object_type'],
                'object_id' => (int) $candidate['object_id'],
                'field' => $field,
                'proposed' => $proposed,
                'reason' => sanitize_text_field($suggestion['reason'] ?? __('AI-assisted SEO improvement.', 'aiwm-web')),
                'risk' => self::sanitize_risk($suggestion['risk'] ?? 'low'),
                'provider' => 'Gemini',
                'model' => $model,
            ];
        }

        if (empty($validated)) {
            return new WP_Error(
                'aiwm_ai_no_valid_suggestions',
                __('Gemini returned no recommendations that match the governed change set.', 'aiwm-web'),
                ['status' => 502, 'provider' => 'Gemini']
            );
        }

        return array_values($validated);
    }

    private function resolve_api_key(): string
    {
        $filtered = apply_filters('aiwm_web_provider_api_key', '', 'gemini');
        if (is_string($filtered) && trim($filtered) !== '') {
            return trim($filtered);
        }
        if (defined('AIWM_GEMINI_API_KEY') && is_string(AIWM_GEMINI_API_KEY)) {
            return trim(AIWM_GEMINI_API_KEY);
        }
        $environment = getenv('AIWM_GEMINI_API_KEY');
        return is_string($environment) ? trim($environment) : '';
    }

    private function resolve_model(): string
    {
        $model = defined('AIWM_GEMINI_MODEL') && is_string(AIWM_GEMINI_MODEL)
            ? AIWM_GEMINI_MODEL
            : self::DEFAULT_MODEL;
        $model = (string) apply_filters('aiwm_web_gemini_model', $model);
        $model = preg_replace('/^models\//i', '', trim($model));
        return $model !== '' ? $model : self::DEFAULT_MODEL;
    }

    private function build_prompt(array $candidates): string
    {
        $json = wp_json_encode($candidates, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        return "You are the recommendation stage of AI WordPress Manager.\n"
            . "Return ONLY a JSON array. Do not mutate anything.\n"
            . "Every output item must match one supplied object_type, object_id and field.\n"
            . "Supported fields are title, excerpt and slug only.\n"
            . "Do not invent object IDs, fields, facts, claims, products, names or statistics.\n"
            . "Keep title and excerpt improvements faithful to the supplied current WordPress text.\n"
            . "For slug, return a concise lowercase hyphenated slug.\n"
            . "Output shape: [{\"object_type\":\"posts|pages\",\"object_id\":123,\"field\":\"title|excerpt|slug\",\"proposed\":\"...\",\"reason\":\"...\",\"risk\":\"low|medium|high\"}].\n"
            . "Candidates:\n" . $json;
    }

    private function provider_error(int $status, string $body): WP_Error
    {
        $friendly = __('Gemini rejected the request.', 'aiwm-web');
        $code = 'aiwm_ai_provider_error';
        if ($status === 400) {
            $friendly = __('Gemini rejected the request or model.', 'aiwm-web');
            $code = 'aiwm_ai_invalid_request';
        } elseif ($status === 401 || $status === 403) {
            $friendly = __('The Gemini API key is invalid, restricted, or unauthorized.', 'aiwm-web');
            $code = 'aiwm_ai_invalid_key';
        } elseif ($status === 429) {
            $friendly = __('The Gemini quota or rate limit was reached.', 'aiwm-web');
            $code = 'aiwm_ai_rate_limited';
        } elseif ($status >= 500) {
            $friendly = __('Gemini is temporarily unavailable.', 'aiwm-web');
            $code = 'aiwm_ai_unavailable';
        }

        $technical = '';
        $decoded = json_decode($body, true);
        if (is_array($decoded) && isset($decoded['error']['message']) && is_string($decoded['error']['message'])) {
            $technical = sanitize_text_field($decoded['error']['message']);
        }

        return new WP_Error($code, $friendly, [
            'status' => $status > 0 ? $status : 502,
            'provider' => 'Gemini',
            'technical' => $technical,
        ]);
    }

    private static function candidate_key(array $candidate): string
    {
        $type = sanitize_key($candidate['object_type'] ?? '');
        $id = absint($candidate['object_id'] ?? 0);
        $field = sanitize_key($candidate['field'] ?? '');
        if (!in_array($type, ['posts', 'pages'], true) || $id < 1 || !in_array($field, ['title', 'excerpt', 'slug'], true)) {
            return '';
        }
        return $type . ':' . $id . ':' . $field;
    }

    private static function sanitize_proposed(string $field, string $value): string
    {
        if ($field === 'slug') {
            return sanitize_title($value);
        }
        if ($field === 'title') {
            return sanitize_text_field($value);
        }
        if ($field === 'excerpt') {
            return sanitize_textarea_field($value);
        }
        return '';
    }

    private static function sanitize_risk(string $risk): string
    {
        $risk = sanitize_key($risk);
        return in_array($risk, ['low', 'medium', 'high'], true) ? $risk : 'medium';
    }
}
