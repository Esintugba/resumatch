#!/usr/bin/env bash
# Read-only post-deploy smoke test, derived from
# docs/operations/staging-smoke-test.md. It creates no data, logs in as nobody
# and never reaches the extraction provider.
#
# Usage: scripts/smoke-test.sh https://resumatch-api.onrender.com
set -uo pipefail

base_url="${1:?Usage: smoke-test.sh <base-url>}"
base_url="${base_url%/}"
curl_options=(--silent --show-error --max-time 30)
failures=0

pass() { echo "ok   - $1"; }
fail() { echo "FAIL - $1"; failures=$((failures + 1)); }

expect_status() {
  local description="$1" expected="$2" path="$3"
  shift 3
  local actual
  actual=$(curl "${curl_options[@]}" --output /dev/null --write-out '%{http_code}' "$@" "$base_url$path")
  if [ "$actual" = "$expected" ]; then
    pass "$description ($actual)"
  else
    fail "$description (expected $expected, got $actual)"
  fi
}

expect_header() {
  local headers="$1" name="$2" expected="$3"
  if printf '%s' "$headers" | tr -d '\r' | grep -qiE "^$name: $expected\$"; then
    pass "header $name"
  else
    fail "header $name (expected '$expected')"
  fi
}

expect_status "/health/live" 200 /health/live

ready_body=$(curl "${curl_options[@]}" "$base_url/health/ready")
if printf '%s' "$ready_body" | jq -e '.status == "Healthy"' > /dev/null; then
  pass "/health/ready is Healthy"
else
  fail "/health/ready is not Healthy: $ready_body"
fi

for check in database schema configuration job-parser; do
  if printf '%s' "$ready_body" | jq -e --arg check "$check" \
    'any(.checks[]; .name == $check and .status != "Unhealthy")' > /dev/null; then
    pass "readiness check $check"
  else
    fail "readiness check $check"
  fi
done

expect_status "missing token is rejected" 401 /api/resumes
expect_status "invalid token is rejected" 401 /api/resumes \
  --header "Authorization: Bearer not-a-valid-token"
expect_status "OpenAPI document is closed" 401 /openapi/v1.json
expect_status "Swagger UI is closed" 401 /swagger/index.html

headers=$(curl "${curl_options[@]}" --dump-header - --output /dev/null "$base_url/health/live")
expect_header "$headers" "x-content-type-options" "nosniff"
expect_header "$headers" "referrer-policy" "no-referrer"
expect_header "$headers" "x-frame-options" "DENY"
expect_header "$headers" "permissions-policy" "camera=\(\), microphone=\(\), geolocation=\(\)"
case "$base_url" in
  https://*) expect_header "$headers" "strict-transport-security" "max-age=[0-9]+.*" ;;
esac

if [ "$failures" -ne 0 ]; then
  echo "$failures smoke check(s) failed."
  exit 1
fi

echo "All smoke checks passed."
