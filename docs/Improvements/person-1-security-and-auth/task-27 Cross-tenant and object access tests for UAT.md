# Task 27: Cross-tenant and object access tests for UAT

| | |
|---|---|
| Priority | 27 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security |
| Depends on | 15 |

## What we found

Tenant isolation is the top security rule of this backend and row-level security plus architecture tests cover the table level well. Two things are not covered by an automated test:

- Access inside one tenant by object id. A user who may read their own leave request should not read a colleague's by changing the id in the URL. The quality gate notes name this as a review focus, which means it depends on people.
- Code paths that call a command without going through the controller attribute. `CLAUDE.md` gives the Inbox providers as a known example.

## What to do

1. Build a small integration test set with two tenants and, inside one tenant, three users: employee, manager of another team, HR.
2. For each module pick the main read-by-id and write-by-id endpoints. Call them with an id from the other tenant and with an id of a colleague's object. Expect 404 or 403, never data.
3. Cover file download endpoints and logo or banner endpoints, which are easy to forget.
4. Cover both SignalR hubs: a user must not be able to join another user's or tenant's group.
5. Check list endpoints with filter parameters: passing another employee's id as a filter must not widen what is returned.
6. Write every finding as its own issue. Do not fix inside this task unless it takes minutes.

## Done when

- The test set runs in the integration suite and covers every module.
- A table of endpoint, test and result is saved in `docs/reports/security/`.
- Any real finding is reported to the team the same day.

## Results (2026-11-19)

Full table: `docs/reports/security/2026-11-19-task-27-cross-tenant-and-object-access.md`.

- The test set is `tests/ONEVO.Tests.Integration/Security/TenantAndObjectAccess/` and runs in the integration
  suite on SQL Server with row-level security on. It has 94 tests and all pass. Two tenants are provisioned and
  signed in through the real flows. Tenant A has an employee, a colleague, a manager of another team (no
  coverage), HR and a covering manager for positive controls.
- Cross-tenant: 38 probes across CoreHR, Org, Roles, Leave, Work, Storage, Support, Dashboard, Conversations and
  Attendance. Every probe returns 403 or 404 with no tenant B data.
- Same tenant: 33 probes for colleague and other-team ids, including file downloads, logos and banners. Every
  probe returns 403 or 404 with no colleague data.
- Ids in a request body, list filters with someone else's id, a session on another tenant's host and both SignalR
  hubs: no leak.
- **Real finding, fixed in this task:** the per-employee monitoring reads (activity, app usage, daily report and
  export, device state, meeting signals, screenshots and their signed URLs) and the on-demand screenshot request
  checked only the permission code, not management coverage. Any `monitoring:read` holder could read any employee
  in the tenant. They now follow the same coverage rule as `/employees/{id}/overview/activity`. There are 12 new
  probes that failed before the fix, a covering-manager positive control, unit tests, and an architecture test
  that keeps new monitoring endpoints gated.
- Productivity report decision resolved (2026-11-23, "coverage only"): `GET /api/v1/monitoring/reports/productivity`
  now sums only the employees a manager covers, in every scope. HR and admins (`org:manage`) keep the
  tenant-wide view. Four integration tests and seven new handler unit tests cover it. See the report's
  "Productivity report" section.

### Not leaks

- Inside a tenant, a colleague's employee id returns 403 while an unknown id returns 404, so anyone can tell an id
  exists. The employee API is documented this way.
- Leave approve or reject: the policy check runs before the approver check. For a request with no policy, a
  non-approver gets 409 instead of 403.
- `employees:read` is a retired permission code, so tests must not seed it.
