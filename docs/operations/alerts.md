# ResuMatch initial alert policy

Thresholds below are starting recommendations and require tuning with production baselines. Dimensions are limited to `provider`, `scope`, `reason`, `outcome`, `check`, and `environment`.

| Alert | Severity | Signal and initial threshold | Window | Operator action / runbook |
|---|---|---|---|---|
| ReadinessFailure | page | readiness unhealthy on >25% replicas | 5 min | Identify failing check; see database/schema runbooks |
| SchemaMismatch | page | `schema` unhealthy on any new replica | immediate | Stop rollout; schema mismatch |
| ParseQueueBacklog | ticket | backlog >500 | 15 min | Parse queue backlog |
| OldPendingDraft | page | oldest pending >10 min | 10 min | Parse queue backlog |
| ProviderCircuitOpen | page | circuit-open/rejected increases | 5 min | Provider circuit open |
| ProviderFailureSpike | page | failures >10% with >=20 requests | 10 min | Provider circuit open |
| ProviderRateLimitSpike | ticket | rate-limit >5% with >=20 requests | 15 min | Provider rate limiting |
| BudgetSystemExhaustion | page | system-scope denial >0 | 5 min | System budget exhausted |
| ReconciliationBacklog | ticket | backlog >100 | 15 min | Reconciliation backlog |
| OldReconciliation | page | oldest reconciliation >15 min | 10 min | Reconciliation backlog |
| UsageOverage | ticket | overage tokens >0 | 15 min | Reconciliation backlog |
| WorkerFailure | page | iteration failures >=3 | 10 min | Kill-switch activation |
| PurgeFailure | ticket | purge failures >=3 | 1 hour | Inspect retention worker and DB health |

Alerts must never group by user or resource identifiers, JWT subject, source text, prompt, evidence, authorization data, or provider request ID.
