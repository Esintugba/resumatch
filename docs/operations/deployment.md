# Production deployment contract

`appsettings.Production.example.json` is documentation, not a secret source. Inject the database connection, JWT key, OpenAI key, and any collector credential from an environment-specific secret manager or platform secret mechanism.

## Probes

- Liveness: `GET /health/live`, initial delay 10 s, period 10 s, timeout 2 s, failure threshold 3.
- Readiness: `GET /health/ready`, initial delay 5 s, period 10 s, timeout 5 s, failure threshold 3.

A readiness failure removes traffic and must not restart the process. Liveness never checks PostgreSQL, schema, provider, budget, circuit, queue, or OTLP. Public health output contains only overall/check status and duration.

## HTTP security settings

Production must override `AllowedHosts` with the explicit public host names (the base `*` makes startup fail) and set the `HttpSecurity` section: `AllowedOrigins` (exact browser origins such as `https://app.example.com`, without path, trailing slash, or default port), `AllowCredentials`, `TrustedProxies`, `TrustedNetworks`, `ForwardLimit`, and the HSTS options (`HstsEnabled` must stay `true`). `appsettings.Production.example.json` shows every field with placeholder values (`example.com` names and RFC 5737 documentation addresses), and a unit test keeps it valid against the options validators. Proxy settings are described in [Reverse proxy and client IP](#reverse-proxy-and-client-ip).

## Reverse proxy and client IP

Behind a load balancer or reverse proxy, `HttpSecurity:TrustedProxies` (exact IPv4 dotted-quad or IPv6 addresses) or `HttpSecurity:TrustedNetworks` (CIDR) is mandatory in production, with `ForwardLimit` matching the number of proxy hops. Forwarded headers are processed only when one of them is set. Without them every request appears to come from the proxy address, so the IP-partitioned login and register rate limits are shared by all users and one client can exhaust them for everyone. The application does not detect this misconfiguration; verify it during the staging smoke test.

## Render + Neon

The free-tier deployment runs the Docker image on a Render web service and uses a Neon PostgreSQL database. Both can change their limits; check [Render free services](https://render.com/docs/free) and [Neon plans](https://neon.com/docs/introduction/plans) before relying on the figures below.

### Platform constraints

- Render free: 512 MB RAM, 0.1 CPU, one instance, 750 instance hours per month. The service spins down after 15 minutes without traffic and takes about a minute to wake. The filesystem is ephemeral, and background work stops while spun down, so queued parse drafts wait for the next request.
- Neon free: 1 GB storage, 100 CU-hours per month, and a compute that suspends after 5 minutes idle. A background poll keeps the compute awake while the API is running, so a short `PollInterval` or frequent uptime pings consume CU-hours.
- Keep both in the same region (for example Render Frankfurt with Neon `aws-eu-central-1`).

### Render settings

Runtime Docker, plan Free, Auto-Deploy off (CI triggers deploys after migrations), health check path `/health/live`. Do not use `/health/ready` as the Render health check: it fails when the database is unavailable or the schema differs, Render restarts instances that fail for 60 seconds, and this contract requires that a readiness failure must not restart the process.

The image listens on `PORT`. Do not set `ASPNETCORE_HTTPS_PORT`: Render already redirects HTTP to HTTPS at its edge, and with no HTTPS port the application does not redirect, which rules out a redirect loop. HSTS is sent when a trusted peer reports `X-Forwarded-Proto: https`.

### Client IP on Render

Set `HttpSecurity__ForwardedForHeaderName=CF-Connecting-IP`. All inbound traffic passes through Cloudflare, which overwrites that header with the real client address. Do not use `X-Forwarded-For` here: Cloudflare appends to it, so a caller controls the leftmost entries, and its multi-hop value cannot satisfy header symmetry with a single `X-Forwarded-Proto`. See [Reverse proxy and client IP](#reverse-proxy-and-client-ip) for the general rule.

Trust only private and loopback peers (`TrustedNetworks` `10.0.0.0/8`, `TrustedProxies` `127.0.0.1` and `::1`). The container is not reachable from the internet directly, and the application accepts the header only from those peers. After changing this, verify on the live service that a spoofed `CF-Connecting-IP` or `X-Forwarded-For` does not reset the login limit. If Render stops fronting services with Cloudflare, this setting must be reviewed: the header would no longer be platform-controlled.

### Environment variables

| Variable | Example | Required | Secret |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Image default | No |
| `AllowedHosts` | `resumatch-api-da3z.onrender.com` | Yes | No |
| `ConnectionStrings__DefaultConnection` | `Host=ep-x-pooler.region.aws.neon.tech;Database=neondb;Username=u;Password=p;SSL Mode=VerifyFull;No Reset On Close=true;Timeout=30;Command Timeout=30` | Yes | Yes |
| `Jwt__SecretKey` | Base64 of at least 32 random bytes | Yes | Yes |
| `HttpSecurity__ForwardedForHeaderName` | `CF-Connecting-IP` | Yes | No |
| `HttpSecurity__TrustedNetworks__0` | `10.0.0.0/8` | Yes | No |
| `HttpSecurity__TrustedProxies__0` / `__1` | `127.0.0.1` / `::1` | Yes | No |
| `HttpSecurity__AllowedOrigins__0` | `https://app.example.com` | Only with a browser client | No |
| `JobDescriptionExtraction__Provider` | `OpenAI` (default `Unavailable`) | To enable parsing | No |
| `JobDescriptionExtraction__ApiKey` | Provider key | With OpenAI | Yes |
| `JobDescriptionExtraction__Model` | Dated model snapshot | With OpenAI | No |
| `JobDescriptionExtraction__RequirePinnedModel`, `__ModelIsPinned` | `true` | With provider execution | No |
| `JobPostingParseDrafts__WorkerEnabled`, `__ProviderExecutionEnabled` | `true` | To enable parsing | No |
| `JobPostingParseDrafts__PollInterval` | `00:00:30` | Recommended | No |
| `AiUsageBudget__Enabled`, `__Reconciliation__Enabled` | `true` | With provider execution | No |
| `AiUsageBudget__User__DailyRequestLimit` / `__DailyTokenLimit` | `5` / `100000` | With provider execution | No |
| `AiUsageBudget__User__MonthlyRequestLimit` / `__MonthlyTokenLimit` | `50` / `1000000` | With provider execution | No |
| `AiUsageBudget__System__DailyRequestLimit` / `__DailyTokenLimit` | `20` / `400000` | With provider execution | No |
| `AiUsageBudget__System__MonthlyRequestLimit` / `__MonthlyTokenLimit` | `200` / `4000000` | With provider execution | No |

Each parse reserves about 16,096 tokens (`EstimatedInputTokens` 12,000 + `MaximumOutputTokens` 4,096), so these limits allow about 6 parses per user per day. Budget tokens are only a safety net; also set a hard spending limit in the OpenAI account.

### Connection strings

The runtime uses Neon's pooled (`-pooler`) host, which runs PgBouncer in transaction mode. `No Reset On Close=true` is required by Npgsql with PgBouncer in transaction mode. The application uses only transaction-scoped features (`SET TRANSACTION`, `pg_advisory_xact_lock`), Npgsql multiplexing is off by default, and protocol-level prepared statements are supported by Neon's pooler. `Timeout=30` leaves room for a suspended compute to wake.

Migrations use the direct host, because Neon does not support pooled connections for `dotnet ef` operations.

Both connection strings use `SSL Mode=VerifyFull`. Do not lower this to `Require` to get past a certificate error: `Require` encrypts the connection without proving which server answered.

### CI deployment

The `deploy` job in `.github/workflows/ci.yml` runs on pushes to `main` after both test jobs. It builds an EF Core migration bundle, proves that the migration connection rejects an unrelated root certificate, applies the bundle to Neon, calls the Render deploy hook for that commit, waits for the new version, waits for `/health/ready`, and runs `scripts/smoke-test.sh`, which is read-only and never calls the extraction provider.

| GitHub setting | Kind | Required |
|---|---|---|
| `NEON_MIGRATION_CONNECTION_STRING` (direct host, `SSL Mode=VerifyFull`) | Repository secret | Yes |
| `RENDER_DEPLOY_HOOK_URL` | Repository secret | Yes |
| `PRODUCTION_URL` | Repository variable | Yes: the service's `https://` origin, currently `https://resumatch-api-da3z.onrender.com`, without a trailing slash |
| `RENDER_API_KEY` | Repository secret | No, and deliberately not set: a Render API key reaches every service in the workspace. Without it CI waits a fixed 5 minutes, so the smoke test may reach the previous version |

GitHub environments and environment secrets are unavailable to private repositories on the Free plan, so these are repository secrets. Only the `deploy` job references them. That job runs only for pushes to `main`, and the workflow does not use `pull_request_target`, so pull request runs never receive them.

Migrations run before the new version starts, so they must stay backward compatible with the running version (see below).

## Expand, deploy, verify

1. Verify backup restoration capability and inspect migration SQL in CI.
2. Confirm the target binary's compiled migration chain and that no unknown database migration exists.
3. Set `ProviderExecutionEnabled=false` to drain new provider claims; keep read and confirmation paths available.
4. Run EF migrations as a controlled CI/CD or operator step. The API never migrates at startup.
5. Verify the migration history and `/health/ready`; the `schema` check must be healthy.
6. Roll out with provider execution disabled, then use the canary sequence below.
7. Verify both probes, telemetry, queue, circuit, budget, and reconciliation signals.
8. Decide rollback using the compatibility check and the runbook; never assume an old binary supports a newer database.

The host shutdown timeout is derived as `ShutdownGracePeriod + 5 seconds`, with hosted services stopped concurrently. The orchestrator termination grace must exceed that derived timeout. Shutdown stops claims, cancels active execution, preserves unknown-cost reservations for reconciliation, releases distributed slots in `finally`, and leaves recoverable leases to expiry when necessary.

## Canary

1. Stage 0: all replicas use `ProviderExecutionEnabled=false`.
2. Stage 1: one canary replica, low local/distributed concurrency and low system budget; enable execution only there.
3. Stage 2: route a small deployment-managed replica percentage to the new binary.
4. Stage 3: proceed to normal rollout after SLO and provenance verification.

The application does not implement percentage routing. Use the kill-switch, readiness, persistent circuit/budget, distributed concurrency and telemetry as rollout controls.

## Rollback

Application rollback and database rollback are separate decisions. `DatabaseAhead` intentionally makes an old binary unready. Prefer forward repair or a compatible binary. Run a database Down migration only after proving its data semantics safe. In particular, a reconciliation ledger containing overage cannot safely downgrade to the older bounded model; never delete or rewrite overage to force rollback.
