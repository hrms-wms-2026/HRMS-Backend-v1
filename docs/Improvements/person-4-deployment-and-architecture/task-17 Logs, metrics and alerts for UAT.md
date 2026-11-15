# Task 17: Logs, metrics and alerts for UAT

| | |
|---|---|
| Priority | 17 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Deployment, Observability |
| Depends on | 05 |

## What we found

- Serilog writes to the console only. There is no central log store configured.
- No OpenTelemetry, no metrics endpoint, no tracing. Nobody can see request rate, error rate or latency without reading raw logs.
- Health checks exist (`/health`, `/health/ready`) but only the database is checked for readiness. Storage, email and the outbox are not.
- A correlation id middleware exists and the frontend sends one, which is a good base.

## What to do

1. Choose the log and metrics target for UAT with the hosting owner (for example Azure Monitor, Grafana stack or Seq). Ship structured JSON logs there.
2. Add OpenTelemetry for ASP.NET Core, HttpClient and SQL client: traces and the standard request duration histogram.
3. Add custom metrics: outbox backlog size, background job duration and failures, rate-limit rejections, sign-in failures.
4. Add readiness checks for object storage and the outbox backlog. Keep liveness free of dependencies.
5. Make sure no personal data or secrets are logged: review request logging, and never log bodies of auth endpoints.
6. Create a first dashboard (requests, p95 per route, 5xx rate, SQL time) and three alerts: 5xx above 1 percent, p95 above 500 ms for 10 minutes, readiness failing.

## Done when

- A request can be followed from the browser correlation id to its SQL calls.
- The dashboard shows p95 per route, which becomes the ongoing check for the 200 ms target.
- The three alerts fire in a test.
