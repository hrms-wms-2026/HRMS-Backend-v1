# Task 10: Response compression and smaller payloads

| | |
|---|---|
| Priority | 10 of 30 (lower number = build first) |
| Owner folder | person-2-performance-and-db |
| Size | 1 person, 1 day |
| Area | Performance, API |
| Depends on | nothing |

## What we found

- `Program.cs` has no `AddResponseCompression`. JSON responses go out uncompressed unless the proxy does it.
- JSON options only add one converter. Null properties are written in full.
- There is no output caching and no `Cache-Control` or `ETag` on responses.

Large list and tree responses (work graph, planner ranges, employee lists) are the ones that suffer most.

## What to do

1. Decide where compression happens: at the reverse proxy (preferred when it exists) or in the API. If in the API, add Brotli and Gzip for `application/json` and `application/problem+json`, enabled for HTTPS.
2. Set `DefaultIgnoreCondition = WhenWritingNull` for API responses. Check with the frontend that no screen relies on explicit `null`.
3. Pick the 10 largest responses from the task 06 baseline. For each, compare the DTO with what the screen actually reads and remove unused fields, or add a slim list DTO next to the detail DTO.
4. Never return full entities with nested collections from a list endpoint.
5. Add `Cache-Control: no-store` on personal data responses and a short private cache with `ETag` on reference data that rarely changes (leave types, work modes, project categories, permission catalogue).

## Done when

- The 10 largest responses are at least 50 percent smaller on the wire.
- A table of before and after sizes is saved in `docs/reports/performance/`.
- Frontend tests still pass.
