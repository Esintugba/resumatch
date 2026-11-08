#!/usr/bin/env bash
# Regenerates the API contract the web app is built against:
#   web/openapi/v1.json       the OpenAPI document served by the API
#   web/src/api/schema.d.ts   TypeScript types generated from it
#
# The API runs briefly on loopback from its Release build. It never reaches a
# real service: the environment is not Development (so user secrets are not
# loaded), the database connection string points at a closed local port, the
# JWT key is random per run and the background jobs that touch the database
# are off. Nothing on the network is contacted except loopback.
#
# Usage: scripts/generate-api-contract.sh [--no-build]
#   --no-build  Use the existing Release build of API/.
#
# Needs the .NET SDK, curl, openssl and `npm ci` already run in web/.
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
build=true
for option in "$@"; do
  case "$option" in
    --no-build) build=false ;;
    *) echo "Usage: $0 [--no-build]" >&2; exit 2 ;;
  esac
done

port=5290
base_url="http://127.0.0.1:$port"
output_dir="$repo_root/API/bin/Release/net10.0"
document="$repo_root/web/openapi/v1.json"

if $build; then
  dotnet build "$repo_root/API/API.csproj" --configuration Release --nologo --verbosity quiet
fi
[ -f "$output_dir/API.dll" ] || { echo "Missing $output_dir/API.dll; build API in Release first." >&2; exit 1; }

log_file="$(mktemp)"
api_pid=""
cleanup() {
  if [ -n "$api_pid" ]; then
    kill "$api_pid" 2>/dev/null || true
    wait "$api_pid" 2>/dev/null || true
  fi
  rm -f "$log_file"
}
trap cleanup EXIT

# The content root is the build output, which carries appsettings.json.
(
  cd "$output_dir"
  export ASPNETCORE_ENVIRONMENT=ContractGeneration
  export DOTNET_ENVIRONMENT=ContractGeneration
  export ASPNETCORE_URLS="$base_url"
  export ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=9;Database=none;Username=none;Password=none"
  Jwt__SecretKey="$(openssl rand -base64 32)"
  export Jwt__SecretKey
  export HttpSecurity__ExposeOpenApi=true
  export AuthSessions__PurgeEnabled=false
  export Observability__Enabled=false
  exec dotnet API.dll
) > "$log_file" 2>&1 &
api_pid=$!

deadline=$((SECONDS + 60))
until curl --silent --fail --output /dev/null "$base_url/health/live"; do
  if ! kill -0 "$api_pid" 2>/dev/null || [ "$SECONDS" -ge "$deadline" ]; then
    echo "The API did not start. Its output:" >&2
    cat "$log_file" >&2
    exit 1
  fi
  sleep 1
done

mkdir -p "$(dirname "$document")"
curl --silent --show-error --fail "$base_url/openapi/v1.json" --output "$document.tmp"
mv "$document.tmp" "$document"
# Keep the file byte-identical across platforms and runs.
sed -i 's/\r$//' "$document"
[ -z "$(tail -c 1 "$document")" ] || echo >> "$document"

(cd "$repo_root/web" && npm run --silent generate:api)
echo "Updated web/openapi/v1.json and web/src/api/schema.d.ts."
