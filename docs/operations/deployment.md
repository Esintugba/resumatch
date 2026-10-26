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
