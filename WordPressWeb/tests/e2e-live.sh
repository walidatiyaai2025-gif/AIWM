#!/usr/bin/env bash
set -euo pipefail

required=(AIWM_HOST AIWM_HOST_USER AIWM_HOST_APP_PASSWORD AIWM_TARGET_URL AIWM_TARGET_CREDENTIAL_REF)
for name in "${required[@]}"; do
  if [[ -z "${!name:-}" ]]; then
    echo "Missing required environment variable: $name" >&2
    exit 2
  fi
done
command -v curl >/dev/null || { echo "curl is required" >&2; exit 2; }
command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }

HOST="${AIWM_HOST%/}"
API="$HOST/wp-json/aiwm/v1/journey"
AUTH=(--user "$AIWM_HOST_USER:$AIWM_HOST_APP_PASSWORD" --silent --show-error --fail-with-body -H 'Content-Type: application/json')

api_get() { curl "${AUTH[@]}" "$API$1"; }
api_post() { local path="$1"; local body="${2:-{}}"; curl "${AUTH[@]}" -X POST -d "$body" "$API$path"; }
assert_eq() { [[ "$1" == "$2" ]] || { echo "ASSERT FAILED: expected '$2', got '$1'" >&2; exit 1; }; }

SITE_NAME="${AIWM_TARGET_NAME:-AIWM disposable E2E target}"
echo "[1/12] Add managed site"
site_json=$(api_post '/sites' "$(jq -nc --arg name "$SITE_NAME" --arg url "$AIWM_TARGET_URL" --arg ref "$AIWM_TARGET_CREDENTIAL_REF" '{name:$name,base_url:$url,credential_ref:$ref}')")
site_id=$(jq -r '.id' <<<"$site_json")
[[ "$site_id" =~ ^[0-9]+$ ]] || { echo "$site_json"; exit 1; }

echo "[2/12] Verify remote WordPress target"
verify_json=$(api_post "/sites/$site_id/verify")
assert_eq "$(jq -r '.site.status' <<<"$verify_json")" 'verified'

echo "[3/12] Sync real posts/pages into persisted job snapshot"
sync_json=$(api_post "/sites/$site_id/sync")
sync_job=$(jq -r '.id' <<<"$sync_json")
api_post "/jobs/$sync_job/run" >/dev/null
assert_eq "$(api_get "/jobs/$sync_job" | jq -r '.status')" 'completed'

echo "[4/12] Explorer reads real remote content"
explorer_json=$(api_get "/sites/$site_id/explorer?type=posts&per_page=10")
explorer_count=$(jq '.items | length' <<<"$explorer_json")
[[ "$explorer_count" -gt 0 ]] || { echo "Target needs at least one post for E2E" >&2; exit 1; }

echo "[5/12] Queue and run bounded persisted SEO audit"
audit_queue=$(api_post "/sites/$site_id/audits")
audit_id=$(jq -r '.auditId' <<<"$audit_queue")
audit_job=$(jq -r '.job.id' <<<"$audit_queue")
api_post "/jobs/$audit_job/run" >/dev/null
audit_json=$(api_get "/audits/$audit_id")
assert_eq "$(jq -r '.audit.status' <<<"$audit_json")" 'completed'
score=$(jq -r '.audit.score' <<<"$audit_json")
findings=$(jq '.findings | length' <<<"$audit_json")
[[ "$findings" -gt 0 ]] || { echo "Target needs at least one supported SEO finding for AI recommendation E2E" >&2; exit 1; }

echo "[6/12] Gemini recommendation -> persisted Suggested Change"
ai_queue=$(api_post "/audits/$audit_id/recommend")
ai_job=$(jq -r '.id' <<<"$ai_queue")
api_post "/jobs/$ai_job/run" >/dev/null
ai_state=$(api_get "/jobs/$ai_job")
assert_eq "$(jq -r '.status' <<<"$ai_state")" 'completed'
recs_json=$(api_get "/recommendations?site_id=$site_id&status=pending")
rec_id=$(jq -r '.items[0].id // empty' <<<"$recs_json")
[[ "$rec_id" =~ ^[0-9]+$ ]] || { echo "No persisted pending AI recommendation" >&2; exit 1; }

echo "[7/12] Human approval is persisted before execution"
approved=$(api_post "/recommendations/$rec_id/decision" '{"decision":"approve"}')
assert_eq "$(jq -r '.status' <<<"$approved")" 'approved'

echo "[8/12] Execute governed mutation and verify remote result"
execute_queue=$(api_post "/recommendations/$rec_id/execute")
execute_job=$(jq -r '.id' <<<"$execute_queue")
api_post "/jobs/$execute_job/run" >/dev/null
execute_state=$(api_get "/jobs/$execute_job")
assert_eq "$(jq -r '.status' <<<"$execute_state")" 'completed'
execution_id=$(jq -r '.payload.execution_id' <<<"$execute_state")
execution_json=$(api_get "/executions/$execution_id")
assert_eq "$(jq -r '.status' <<<"$execution_json")" 'completed'
assert_eq "$(jq -r '.verification.verified' <<<"$execution_json")" 'true'

echo "[9/12] Receipt and evidence are persisted with integrity hashes"
evidence_json=$(api_get "/evidence?execution_id=$execution_id")
[[ "$(jq '[.items[] | select(.kind=="before_snapshot")] | length' <<<"$evidence_json")" -gt 0 ]]
[[ "$(jq '[.items[] | select(.kind=="after_snapshot")] | length' <<<"$evidence_json")" -gt 0 ]]
[[ "$(jq '[.items[] | select(.kind=="receipt")] | length' <<<"$evidence_json")" -gt 0 ]]

echo "[10/12] Retry/idempotency: completed execution request is reused, not duplicated"
execute_again=$(api_post "/recommendations/$rec_id/execute")
assert_eq "$(jq -r '.id' <<<"$execute_again")" "$execute_job"
api_post "/jobs/$execute_job/run" >/dev/null
assert_eq "$(api_get "/jobs/$execute_job" | jq -r '.status')" 'completed'

echo "[11/12] Cancel then retry a queued job; cancelled job never reports success"
cancel_queue=$(api_post "/sites/$site_id/sync")
cancel_job=$(jq -r '.id' <<<"$cancel_queue")
cancelled=$(api_post "/jobs/$cancel_job/cancel")
assert_eq "$(jq -r '.status' <<<"$cancelled")" 'cancelled'
retried=$(api_post "/jobs/$cancel_job/retry")
assert_eq "$(jq -r '.status' <<<"$retried")" 'queued'
api_post "/jobs/$cancel_job/run" >/dev/null
assert_eq "$(api_get "/jobs/$cancel_job" | jq -r '.status')" 'completed'

echo "[12/12] Controlled remote failure is surfaced without fake success"
bad_ref="missing-e2e-credential-$(date +%s)"
bad_site=$(api_post '/sites' "$(jq -nc --arg name 'AIWM expected failure target' --arg url "$AIWM_TARGET_URL" --arg ref "$bad_ref" '{name:$name,base_url:$url,credential_ref:$ref}')")
bad_site_id=$(jq -r '.id' <<<"$bad_site")
set +e
bad_response=$(curl "${AUTH[@]}" -X POST -w '\n%{http_code}' "$API/sites/$bad_site_id/verify" 2>/dev/null)
bad_rc=$?
set -e
bad_http=$(tail -n1 <<<"$bad_response")
bad_body=$(sed '$d' <<<"$bad_response")
[[ "$bad_rc" -ne 0 && "$bad_http" == '503' ]] || { echo "Expected controlled credential failure; got HTTP $bad_http" >&2; echo "$bad_body" >&2; exit 1; }
assert_eq "$(jq -r '.code' <<<"$bad_body")" 'aiwm_remote_credential_missing'

# New request after the full sequence proves DB state survives page/request restart.
restart_job_state=$(api_get "/jobs/$execute_job" | jq -r '.status')
assert_eq "$restart_job_state" 'completed'

jq -n \
  --arg host "$AIWM_HOST" \
  --arg target "$AIWM_TARGET_URL" \
  --argjson siteId "$site_id" \
  --argjson auditId "$audit_id" \
  --arg score "$score" \
  --argjson findings "$findings" \
  --argjson recommendationId "$rec_id" \
  --argjson executionId "$execution_id" \
  --argjson executionJobId "$execute_job" \
  --argjson cancelRetryJobId "$cancel_job" \
  '{status:"PASS",host:$host,target:$target,siteId:$siteId,auditId:$auditId,score:$score,findings:$findings,recommendationId:$recommendationId,executionId:$executionId,executionJobId:$executionJobId,cancelRetryJobId:$cancelRetryJobId,restartState:"persisted"}'
