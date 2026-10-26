# Production deployment contract

`appsettings.Production.example.json` is documentation, not a secret source. Inject the database connection, JWT key, OpenAI key, and any collector credential from an environment-specific secret manager or platform secret mechanism.

## Probes

- Liveness: `GET /health/live`, initial delay 10 s, period 10 s, timeout 2 s, failure threshold 3.
- Readiness: `GET /health/ready`, initial delay 5 s, period 10 s, timeout 5 s, failure threshold 3.

A readiness failure removes traffic and must not restart the process. Liveness never checks PostgreSQL, schema, provider, budget, circuit, queue, or OTLP. Public health output contains only overall/check status and duration.

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
