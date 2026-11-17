# Task 20: Fewer, smaller requests for dashboard and calendar

| | |
|---|---|
| Priority | 20 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | API, Performance |
| Depends on | 06 |

## What we found

- The dashboard loads widget data one widget at a time through `GET /api/v1/dashboard-widgets/{instanceId}/data`. A dashboard with 12 widgets makes 12 requests, and each request repeats authentication and tenant set-up.
- `DashboardLayoutController` already has `GET {id}/bootstrap`. Check what it returns; it may be the right place to carry the first screen of widget data.
- Planner and calendar have many range endpoints (`planner/me/range`, `team/range`, `team/week`, `team/schedule`, `employees/{id}/range`, project calendar). The frontend can ask for any range, so one call may load a full year.

## What to do

1. Add a batch endpoint: one request with a list of widget instance ids, one response with a result per widget. Run the providers in parallel where they do not share a DbContext, or one after another on one connection.
2. A slow or failing widget must not block the others: return a per-widget status.
3. Cap every range endpoint on the server (for example 62 days). Return 400 for a larger range.
4. For team range endpoints, page by employee when the team is large.
5. Check each range endpoint returns only the fields the calendar draws. Details load when the user opens an item.
6. Agree the new contract with the frontend (frontend task 18).

## Done when

- A 12-widget dashboard loads its data in one or two requests.
- No range endpoint accepts more than the agreed span.
- Timings before and after are recorded.
