# Staging smoke-test checklist

Run after schema migration and before production expansion. Record outcomes without source text or identifiers.

- [ ] `/health/live` returns 200 independently of external dependencies.
- [ ] `/health/ready` returns 200 and lists healthy `database`, `schema`, `configuration`, and `job-parser` checks.
- [ ] `/health/version` reports the deployed commit, not `unknown`.
- [ ] JWT authentication accepts a valid token and rejects missing/invalid tokens.
- [ ] Parse request creates a draft and respects ownership, ETag, cooldown, and rate limiting.
- [ ] Worker claims with lease fencing and transitions through provider execution to Ready.
- [ ] Confirmation persists only reviewed skills and correct provenance.
- [ ] Analysis uses the confirmed source snapshot and deterministic ATS version.
- [ ] Budget reservation settles, or uncertain usage enters reconciliation; distributed slot is released.
- [ ] Circuit state and bounded metrics/traces are visible; prompt **version**, never prompt content, is observable.
- [ ] OTLP export interruption does not break an API request, worker correctness, or readiness.
- [ ] Kill-switch prevents new provider work while reads and confirmation remain available.

A real-provider smoke test requires manual approval, one/few requests, low concurrency and budget, a pinned model declaration, and a ready kill-switch. Never use production source data. Restore `ProviderExecutionEnabled=false` if any safety check fails.
