# Task 00c: Inbox list and filters paged in the database

| | |
|---|---|
| Priority | Urgent. Do before task 01. Added 2026-11-15 after measuring |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day for the design and the two largest providers. The remaining providers are follow-up days |
| Area | Architecture, Performance, Pagination |
| Depends on | 00a (shares the provider contract change) |

## What we found

Measured on 2026-11-15 (`docs/reports/performance/2026-11-15-api-response-timings.md`):

- `GET /api/v1/inbox?pageSize=25` takes 3.1 to 3.4 seconds to return 25 rows.
- `GET /api/v1/inbox/filters` takes 3.1 seconds to return 7.7 KB of filter options.

Cause, in `InboxRouter`:

- `CollectAsync` loops over every source provider and calls `provider.ListAsync`. Each provider returns all of the caller's items. The router then filters (`Matches`), sorts and does `Skip` and `Take` in memory. Asking for 25 rows costs the same as asking for all of them.
- `GetFiltersAsync` loops over every provider and calls `GetFilterFieldsAsync`, which runs the providers' own queries each time, although filter options change rarely.
- The design gets slower with every new module and with every month of history.

This is an architecture problem in the provider contract, not a slow query to tune.

## What to do

1. Change the provider contract so paging and filtering happen in the provider's SQL: `ListAsync(query, limit, cursor)` returns at most `limit` rows already sorted, plus a cursor.
2. In the router, merge the providers' pages by the sort key (a k-way merge): ask each provider for one page, take the first N across all, and remember a cursor per provider for the next page.
3. Push the common filters (status, direction, category, date range, source, person) down to the providers. Keep `Matches` only as a safety check.
4. Use the count methods from task 00a for the total instead of `sorted.Count`.
5. Convert the two providers with the most rows first. Find them by logging the item count per provider once for the dapi owner user. Leave the others on an adapter that wraps the old method, and list them as follow-up tasks.
6. Filters: cache each provider's filter fields per tenant for a few minutes and clear the entry when the underlying catalogue changes (leave types, categories, work modes).
7. Consider a longer-term option and write it down, do not build it here: one `inbox_items` read table kept up to date by the outbox, so the inbox is a single indexed query.

## Done when

- `GET /inbox?pageSize=25` answers in under 300 ms for the dapi owner user with the two largest providers converted, and the path to under 200 ms is written down.
- `GET /inbox/filters` answers in under 200 ms on a repeat call.
- Paging through the whole inbox returns every item exactly once (test).
- The remaining providers are listed with an owner.
