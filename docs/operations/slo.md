# ResuMatch production SLOs

These are initial production targets. Revisit them after four weeks of representative traffic; they are not immutable product requirements.

| SLI | Initial target | Window | Signal |
|---|---:|---:|---|
| API request availability | 99.9% non-5xx | 30 days | ASP.NET Core request metrics |
| API latency | 95% below 500 ms | 30 days | ASP.NET Core request duration |
| Accepted parse completion | 95% Ready within 5 min | 7 days | accepted/ready counters and queue age |
| Pending queue age | 99% below 10 min | 7 days | `resumatch.job_parsing.queue.oldest_age` |
| Provider success | 99% excluding caller cancellation | 7 days | provider request/outcome counters |
| Reconciliation age | 99% below 15 min | 7 days | reconciliation oldest-age gauge |
| Database readiness | 99.95% healthy | 30 days | `database` readiness check |
| Schema compatibility | 100% during serving | deployment | `schema` readiness check |

Parse accepted rate is derived from bounded ASP.NET route/status metrics; the pipeline also tracks backlog, Ready, retry, failure and superseded counters. Provider SLIs use bounded `provider` and `outcome` dimensions for latency, timeout, rate-limit, unavailable, circuit and distributed-capacity signals. Budget SLIs use denial scope, reconciliation reason, backlog age and overage-token counters. No user, draft, job, reservation, prompt, evidence, or provider-request identifier is an allowed metric dimension.

OTLP delivery is deliberately outside readiness. Export loss reduces observability but cannot turn a successful API or worker operation into a failure.
