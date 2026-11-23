# Logs, metrics and alerts

Task 17 (`docs/Improvements/person-4-deployment-and-architecture/task-17 Logs, metrics and alerts for UAT.md`).
How the API reports what it does, where that goes, and the dashboard and alerts to set up for UAT.

## Where telemetry goes

The target is chosen by setting, so one build serves every option
(`src/ONEVO.Api/Extensions/ObservabilityExtensions.cs`):

| Setting | Result |
|---|---|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Traces, metrics and logs go to Application Insights (Azure Monitor OpenTelemetry distro). Wins when both are set |
| `OTEL_EXPORTER_OTLP_ENDPOINT` (plus the standard `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_EXPORTER_OTLP_PROTOCOL`) | Traces, metrics and logs go to any OTLP backend: Grafana Cloud, Seq, an OpenTelemetry Collector |
| neither | Nothing is exported. Logs go to the console only, as before |

Both are secrets when they carry a key (the connection string, OTLP headers with an API key).

**For UAT on App Service:** your access covers the App Service only, so the subscription owner has to create an
Application Insights resource (workspace-based) and give you its connection string. Put it in the App Service
settings as `APPLICATIONINSIGHTS_CONNECTION_STRING`. No code change or redeploy is needed beyond the settings
restart. Until then the console logs are in App Service → Log stream.

## What is collected

**Logs.** Serilog writes one JSON object per line outside Development (`RenderedCompactJsonFormatter`), readable
text in Development. With an exporter, the same events also go to the backend, joined to the trace they belong
to. Every request log line carries `CorrelationId`, `TenantId` and `UserId` (ids, no names).

**Traces.** Incoming requests (ASP.NET Core), outgoing HTTP calls (email, storage, calendar providers) and SQL
commands, as one trace per request. `/health` and `/health/ready` are not traced. The request span carries the
browser's correlation id as `crownus.correlation_id` (the `X-Correlation-Id` header the frontend sends, capped
at 100 characters). Query strings are redacted by the instrumentation.

**Standard metrics.** `http.server.request.duration` (histogram with `http.route` and status), outgoing HTTP
duration, .NET runtime (GC, thread pool, exceptions).

**Crownus metrics** (meter `Crownus.Api`, `src/ONEVO.Application/Common/Observability/CrownusMetrics.cs`):

| Metric | Type | Tags | Source |
|---|---|---|---|
| `crownus.jobs.runs` | counter | `job`, `outcome` (completed, failed, timed_out, skipped) | every job run (`ExclusiveJobRunner`, task 13) |
| `crownus.jobs.duration` | histogram, ms | `job`, `outcome` | runs that did work |
| `crownus.outbox.pending` | gauge | | outbox rows waiting |
| `crownus.outbox.failed` | gauge | | outbox rows that gave up (task 31 adds the clean-up) |
| `crownus.outbox.oldest_pending_age` | gauge, s | | age of the oldest waiting row |
| `crownus.ratelimit.rejections` | counter | `limiter` (api, auth), `partition` | every 429 |
| `crownus.auth.signin_failures` | counter | `surface` (tenant, admin), `status` | failed password and Google sign-ins |

No tag carries a name, email or other personal data. The outbox gauges come from one cached query (30 s), shared
with the readiness check.

## Health checks

| URL | Checks | Status |
|---|---|---|
| `/health` | process only | liveness: never depends on anything else |
| `/health/ready` | SQL Server | **Unhealthy (503)** when unreachable: this instance cannot serve |
| | object storage (a read-only "does this key exist" call, 5 s limit) | **Degraded (still 200)** on failure |
| | outbox (oldest waiting message over 15 minutes, or unreadable) | **Degraded (still 200)** |
| | Redis, only when configured (task 21, [shared-state.md](shared-state.md)) | **Degraded (still 200)** when disconnected or not answering a ping |

Storage and outbox problems affect every instance alike. Failing readiness for them would take the whole site out
of the load balancer for a problem that only affects file features or delays notifications, so they report
Degraded. Alert on Degraded instead (below). A local run without storage keys reports Degraded; that is expected.

## Dashboard (Application Insights)

Workbook or dashboard tiles, from these queries (Logs, workspace-based Application Insights tables):

```kusto
// Requests per minute and 5xx rate
AppRequests
| where TimeGenerated > ago(24h)
| summarize requests = count(), errors5xx = countif(toint(ResultCode) >= 500) by bin(TimeGenerated, 5m)
| extend errorRatePct = round(100.0 * errors5xx / requests, 2)
```

```kusto
// p95 per route: the ongoing check of the 200 ms target
AppRequests
| where TimeGenerated > ago(24h)
| summarize p95_ms = percentile(DurationMs, 95), requests = count() by Name
| where requests > 20
| order by p95_ms desc
```

```kusto
// SQL time per route (dependency time inside requests)
AppDependencies
| where TimeGenerated > ago(24h) and DependencyType == "SQL"
| join kind=inner (AppRequests | project OperationId, Route = Name) on OperationId
| summarize sql_ms_p95 = percentile(DurationMs, 95), calls = count() by Route
| order by sql_ms_p95 desc
```

```kusto
// Background jobs: runs and failures
AppMetrics
| where Name in ("crownus.jobs.runs", "crownus.jobs.duration")
| extend job = tostring(Properties["job"]), outcome = tostring(Properties["outcome"])
| summarize total = sum(Sum) by Name, job, outcome, bin(TimeGenerated, 1h)
```

**Follow one request:** take the `X-Correlation-Id` from the browser's network tab, then

```kusto
let cid = "<correlation id>";
let op = AppRequests | where Properties["crownus.correlation_id"] == cid | project OperationId;
union AppRequests, AppDependencies, AppTraces, AppExceptions
| where OperationId in (op)
| order by TimeGenerated asc
```

This was checked in code, not yet in Application Insights: `TracingIntegrationTests` boots the API with tracing
on and finds the sign-in request's span by its correlation id, with its SQL spans in the same trace.

## Alerts

Create these as log search alert rules on the Application Insights resource, with an action group that emails
the team:

| Alert | Query | Evaluate | Fire when |
|---|---|---|---|
| 5xx above 1 % | `AppRequests \| summarize total = count(), bad = countif(toint(ResultCode) >= 500) \| extend pct = 100.0 * bad / total \| where total > 50 and pct > 1` | every 5 min over 10 min | 1 row |
| p95 above 500 ms for 10 minutes | `AppRequests \| summarize p95 = percentile(DurationMs, 95), total = count() \| where total > 50 and p95 > 500` | every 5 min over 10 min | 1 row |
| Readiness failing | Availability test (standard test) on `https://<host>/health/ready` from 3 locations, 5-minute frequency | | 2 locations fail |
| Degraded readiness (warning) | Same availability test with "content match" on `Healthy` | | 2 locations fail |

The thresholds come from the task. Test each alert once by lowering its threshold, then set it back; write the date
in the release ticket.

## Personal data and secrets in logs (review, 2026-11-20)

| Finding | Status |
|---|---|
| Request logging (`UseSerilogRequestLogging`) logs method, path (no query string), status, time, tenant and user ids | Fine |
| Request bodies of auth endpoints are never logged. `AuthRateLimitingMiddleware` reads the login body to key its limit and does not log it | Fine |
| EF Core sensitive data logging (parameter values) is not enabled anywhere | Fine |
| Four email log lines wrote the full recipient address (`PlatformKeyTransactionalEmailSender`, `ResendEmailAdapter`, `SendGridEmailAdapter`, `TransactionalEmailService`) | **Fixed:** masked to `j***@acme.com` (`LogRedaction.MaskEmail`) |
| Calendar clients log the provider's error body on a failed call (`CalendarOAuthTokenExchangeClient`, `MicrosoftGraphMeetingClient`, `ZoomMeetingClient`). OAuth error bodies carry no tokens, but a Graph or Zoom error message can name a user | **Follow-up:** log the error code only, or cap and scrub the body |
| Trace query strings | Redacted by the OpenTelemetry instrumentation by default |

## Not done yet

- The Application Insights resource, dashboard and alerts exist only as the definitions above until the owner
  creates the resource.
- The three alerts have not fired in a test.
