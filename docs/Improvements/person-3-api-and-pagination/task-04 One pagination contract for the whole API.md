# Task 04: One pagination contract for the whole API

| | |
|---|---|
| Priority | 4 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | API, Pagination |
| Depends on | nothing |

## What we found

Pagination exists in some places but every module does it its own way:

- Query names: `page` (55 uses), `pageSize` (51), `limit` (8), `pageNumber` (6), `take` (4), `offset` (1).
- Default page sizes: 10, 20, 25 and 100.
- `MaxPageSize` is declared separately in many files with values 50, 100 and 500.
- `PagedResult<T>` exists in `Application/Common/Models` and is the most used shape, but not the only one.

## What to do

1. Write the standard in `docs/api/pagination-standard.md`: query names `page` (starts at 1) and `pageSize`; default 25; maximum 100; response is `PagedResult<T>` with items, page, pageSize, totalCount.
2. Add one shared `PageRequest` type in `Application/Common/Models` that clamps the values in one place, and one `ToPagedResultAsync` extension for `IQueryable<T>` in Infrastructure.
3. Decide the rule for very large or live lists (activity feeds, notifications, chat): use a cursor (`after` plus `limit`) instead of page numbers. Write that rule in the same doc.
4. Decide when `totalCount` may be skipped (it costs a second query).
5. Add an architecture test that fails when a controller action uses `limit`, `take`, `offset` or `pageNumber` as a query name.
6. Move two existing endpoints to the shared types as the example for tasks 08 and 12.

## Done when

- The doc is merged and the shared types exist with unit tests.
- Two endpoints use them.
- The frontend team has confirmed the contract (frontend task 03 depends on it).
