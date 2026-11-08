#!/usr/bin/env bash
# Runs the web app's full-stack Playwright tests (web/e2e-fullstack) against a
# real API and a throwaway PostgreSQL container.
#
# - PostgreSQL: a new container on 127.0.0.1:55432 with a random password,
#   removed afterwards.
# - API: the Release build on http://localhost:5252 in an "E2E" environment.
#   Not Development, so user secrets are never loaded; the JWT key is random
#   per run and no external service is configured.
# - Web: built and served by `vite preview --mode e2e` on https://localhost:4173
#   (self-signed certificate; WebKit sends Secure cookies only over HTTPS), which
#   proxies /api and /health to the API, as `npm run dev` does.
#
# Usage: scripts/run-e2e-fullstack.sh [extra Playwright arguments]
# Needs Docker, the .NET SDK, curl, openssl and `npm ci` in web/ (with
# `npx playwright install chromium webkit` once).
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
api_url="http://localhost:5252"
db_port=55432
container="resumatch-e2e-$(openssl rand -hex 4)"
db_password="$(openssl rand -hex 16)"
connection="Host=127.0.0.1;Port=$db_port;Database=resumatch_e2e;Username=postgres;Password=$db_password"
api_pid=""
api_log="$(mktemp)"

cleanup() {
  if [ -n "$api_pid" ]; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  docker rm --force "$container" > /dev/null 2>&1 || true
  rm -f "$api_log"
}
trap cleanup EXIT

for port in 5252 4173 "$db_port"; do
  if curl --silent --max-time 2 --output /dev/null "http://127.0.0.1:$port/"; then
    echo "Port $port is in use. Stop the process on it first (for example a running API or preview server)." >&2
    exit 1
  fi
done

echo "--- Starting PostgreSQL ($container)"
docker run --detach --rm --name "$container" \
  --env POSTGRES_PASSWORD="$db_password" --env POSTGRES_DB=resumatch_e2e \
  --publish "127.0.0.1:$db_port:5432" postgres:17.6-alpine > /dev/null
# Over TCP: the image first runs a temporary server on the Unix socket only,
# then restarts, so a socket check can succeed too early.
ready=false
for _ in $(seq 1 60); do
  if docker exec "$container" pg_isready --host 127.0.0.1 --username postgres --dbname resumatch_e2e > /dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
if ! $ready; then
  echo "PostgreSQL did not become ready. Its log:" >&2
  docker logs "$container" >&2 || true
  exit 1
fi

# Settings for both the migration and the API run. Environment variables
# only; nothing is written to disk.
export ASPNETCORE_ENVIRONMENT=E2E
export DOTNET_ENVIRONMENT=E2E
export ConnectionStrings__DefaultConnection="$connection"
Jwt__SecretKey="$(openssl rand -base64 32)"
export Jwt__SecretKey
export HttpSecurity__AllowedOrigins__0="https://localhost:4173"
export HttpSecurity__AllowCredentials=true
export AuthenticationRateLimits__LoginPermitLimit=50
export AuthenticationRateLimits__RegisterPermitLimit=50
export Observability__Enabled=false

echo "--- Building the API and applying migrations"
dotnet build "$repo_root/API/API.csproj" --configuration Release --nologo --verbosity quiet
(cd "$repo_root" && dotnet tool restore > /dev/null && \
  dotnet ef database update --project API --configuration Release --no-build --connection "$connection" > /dev/null)

echo "--- Starting the API on $api_url"
(
  cd "$repo_root/API/bin/Release/net10.0"
  export ASPNETCORE_URLS="$api_url"
  exec dotnet API.dll
) > "$api_log" 2>&1 &
api_pid=$!

deadline=$((SECONDS + 90))
until [ "$(curl --silent --output /dev/null --write-out '%{http_code}' "$api_url/health/ready" || true)" = "200" ]; do
  if ! kill -0 "$api_pid" 2>/dev/null || [ "$SECONDS" -ge "$deadline" ]; then
    echo "The API did not become ready. Its output:" >&2
    cat "$api_log" >&2
    exit 1
  fi
  sleep 1
done

echo "--- Running Playwright"
cd "$repo_root/web"
npx playwright test --config playwright.fullstack.config.ts "$@"
