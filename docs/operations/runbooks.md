# ResuMatch operational runbooks

Each section follows Signal, likely causes, immediate safe action, what not to do, verification, recovery, and escalation.

## Schema mismatch

- **Signal:** `schema` readiness unhealthy.
- **Likely causes:** database behind, database ahead, missing/unreadable migration history.
- **Immediate safe action:** stop rollout and compare artifact migration metadata with read-only history.
- **Do not:** start the API with auto-migration, expose migration names publicly, or edit history rows.
- **Verification:** controlled migration tooling plus `/health/ready`.
- **Recovery:** apply reviewed forward migration or deploy the compatible binary.
- **Escalation:** database owner and release owner.

## Database unavailable

- **Signal:** `database` readiness unhealthy while liveness stays healthy.
- **Likely causes:** PostgreSQL, DNS/network, credentials, connection exhaustion.
- **Immediate safe action:** remove affected replicas through readiness and inspect platform/database signals.
- **Do not:** restart-loop healthy processes or print connection strings.
- **Verification:** read-only connectivity and readiness.
- **Recovery:** restore dependency, credentials, or capacity.
- **Escalation:** database/platform owner.

## Provider circuit open or rate limiting

- **Signal:** circuit-open/rejected, rate-limit, timeout, or unavailable counters rise.
- **Likely causes:** provider incident, quota, latency, invalid rollout configuration.
- **Immediate safe action:** set `ProviderExecutionEnabled=false` when sustained; preserve reads and confirmations.
- **Do not:** bypass the circuit, distributed slots, or persistent budget with database edits.
- **Verification:** no new provider calls/reservations/attempts; queue may grow; drafts are not marked failed.
- **Recovery:** validate provider, budget and circuit state; re-enable on one canary first.
- **Escalation:** provider and application owners.

## System budget exhausted

- **Signal:** system-scope budget denial.
- **Likely causes:** expected cap, traffic spike, usage settlement anomaly.
- **Immediate safe action:** keep the cap; inspect aggregate bounded metrics and reconciliation.
- **Do not:** delete ledger rows or raise limits without cost approval.
- **Verification:** reservation/settlement/reconciliation counts reconcile.
- **Recovery:** approved limit change or next budget period.
- **Escalation:** product/cost owner.

## Reconciliation backlog

- **Signal:** backlog/oldest-age/overage alerts.
- **Likely causes:** provider response uncertainty, worker/DB failure, insufficient reconciliation capacity.
- **Immediate safe action:** retain unknown-cost reservations and restore reconciliation processing.
- **Do not:** release unknown usage or purge unresolved ledger entries.
- **Verification:** backlog and age decline; overage remains auditable.
- **Recovery:** controlled worker capacity and root-cause correction.
- **Escalation:** application and cost owners.

## Parse queue backlog

- **Signal:** pending count or oldest age above threshold.
- **Likely causes:** kill-switch, circuit, capacity, provider, budget, database.
- **Immediate safe action:** identify bounded reason signals before adding capacity.
- **Do not:** bypass claim/lease fencing or manually alter attempt counts.
- **Verification:** Ready throughput exceeds accepted rate and age falls.
- **Recovery:** resolve dependency, then gradual canary enablement.
- **Escalation:** application/provider owner.

## Kill-switch activation and recovery

- **Signal:** provider instability, uncontrolled cost, or deployment drain.
- **Immediate safe action:** set `JobPostingParseDrafts:ProviderExecutionEnabled=false` through the runtime-reloaded configuration source.
- **Verification:** no new call, reservation, distributed slot or attempt; reads and confirmation work; liveness is healthy; readiness may be Degraded maintenance but remains serving-compatible.
- **Do not:** mark queued drafts failed or modify circuit/budget records.
- **Recovery:** inspect queue/circuit/budget, enable one canary, observe, then expand.
- **Escalation:** incident commander for broad re-enable.

## Canary rollback and migration rollback

- **Signal:** SLO regression, invalid output, budget/circuit anomaly, or readiness failure.
- **Immediate safe action:** disable provider execution and route away from canary.
- **Do not:** roll back the binary while `schema` would report DatabaseAhead, or destructively downgrade data.
- **Verification:** stable old cohort plus compatible schema.
- **Recovery:** forward fix is preferred. Database rollback requires reviewed safe Down semantics and restored-backup capability.
- **Escalation:** release and database owners.

## API key rotation

Provision the new key in the external secret mechanism, deploy/restart because extraction options are not hot-reloaded, verify one canary, then revoke the old key. Never log either raw key or authorization header.
