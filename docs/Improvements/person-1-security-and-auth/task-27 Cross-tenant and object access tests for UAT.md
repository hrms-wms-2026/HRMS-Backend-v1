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
