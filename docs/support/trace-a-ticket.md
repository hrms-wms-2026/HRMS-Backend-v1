# Trace a support ticket to its failed requests

For platform support staff. A tenant ticket can carry the references (correlation ids) of the user's recent
failed requests (DEC-114). This page turns a reference into the request, its error and its SQL calls in
Application Insights. Setup of Application Insights and what is collected: [../deployment/observability.md](../deployment/observability.md).

## 1. Get the reference

- **Admin console:** open the ticket. The detail lists each reference with a copy button. The API field is
  `correlation_ids` on `GET /admin/v1/support/tickets/{id}`.
- **From the user:** the error message in the tenant app shows `Reference: <first 8 characters>`; the copy
  button copies the full id. Ask for the full id if they only read out the short one.
- A reference is the `X-Correlation-Id` of that request. The API returns it in every error body
  (`correlationId`) and response header, writes it on every log line (`CorrelationId`) and on the request
  span (`crownus.correlation_id`).

## 2. Find the request

Application Insights → **Logs**, last 7 days (or the ticket's date), then:

```kusto
let cid = "<reference>";
union traces, requests, exceptions
| where customDimensions.CorrelationId == cid or customDimensions["crownus.correlation_id"] == cid
| order by timestamp asc
```

If you query the Log Analytics workspace instead of the Application Insights resource, the tables are
`AppTraces`, `AppRequests`, `AppExceptions` and the column is `Properties` instead of `customDimensions`
(see the "Follow one request" query in [../deployment/observability.md](../deployment/observability.md)).

No rows: check the time range, then whether sampling dropped it. Failed requests and exceptions are meant to be
kept; successful traces may be sampled.

## 3. What to read

| Look at | Where | Tells you |
|---|---|---|
| Route and status | `requests` row: `name` (e.g. `POST api/v1/payroll/...`), `resultCode`, `duration` | Which action failed and how (400 validation, 403 permission, 404, 409 conflict, 500 server error) |
| Exception | `exceptions` row: `type`, `outerMessage`, `details` | The server error and the code line for a 500 |
| Log lines | `traces` rows in time order | Validation messages, warnings, handler log lines, `TenantId` and `UserId` |
| SQL calls | `dependencies` of the same operation (query below) | Slow or failing SQL, timeouts, deadlocks |

SQL and outgoing HTTP calls of the same request share its `operation_Id`:

```kusto
let cid = "<reference>";
let op = requests | where customDimensions["crownus.correlation_id"] == cid | project operation_Id;
dependencies
| where operation_Id in (op)
| project timestamp, type, target, name, data, duration, success, resultCode
| order by timestamp asc
```

Typical reading:

- **4xx** with no exception: the request was refused on purpose. The `traces` line or the error body says why
  (missing permission, validation, wrong state). Usually a how-to answer for the customer.
- **5xx** with an exception: a defect. Copy the exception type, message and route into the bug, with the reference.
- **Timeout / slow:** look at `duration` on the request and on the SQL `dependencies`.

## 4. Without a reference

Filter by tenant, user and time. `TenantId` and `UserId` are ids, not names; get them from the ticket
(`tenant_id`, `created_by_user_id` in the admin ticket detail).

```kusto
traces
| where timestamp between (datetime(<from>) .. datetime(<to>))
| where customDimensions.TenantId == "<tenant id>" and customDimensions.UserId == "<user id>"
| where severityLevel >= 2 or toint(customDimensions.StatusCode) >= 400
| project timestamp, message, customDimensions.CorrelationId, customDimensions.StatusCode, customDimensions.RequestPath
| order by timestamp desc
```

Each row's `CorrelationId` then goes into step 2.

## 5. What to tell the customer

- Say what failed in their words (the screen and action), not the route or the exception.
- 4xx: what to do differently (permission to ask for, field to fix).
- 5xx: that it is a fault on our side, logged with their reference, and when they will hear back.
- Never send log lines, SQL, stack traces or other tenants' data to the customer.

## Notes

- References are stored on the ticket only (`support_tickets.correlation_ids`). They are not copied into audit
  rows.
- A reference is up to 100 characters of `A-Z a-z 0-9 -`; a ticket keeps at most 5.
