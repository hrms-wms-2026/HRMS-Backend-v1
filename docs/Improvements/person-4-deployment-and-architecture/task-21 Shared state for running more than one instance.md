# Task 21: Shared state for running more than one instance

| | |
|---|---|
| Priority | 21 of 30 (lower number = build first) |
| Owner folder | person-4-deployment-and-architecture |
| Size | 1 person, 1 day |
| Area | Scaling, Architecture |
| Depends on | 13 |

## What we found

These parts keep state in the memory of one process. With two instances behind a load balancer each instance sees only its own half:

- `PublicApiRateLimitingMiddleware` and `AuthRateLimitingMiddleware` (the second one says so in its own comment: "valid only while deployment is restricted to one API instance").
- `WebActivityTracker`: who is active in the web app.
- `TrayConnectionTracker`: which tray devices are connected. Presence enforcement depends on it.
- `AddSignalR()` with no backplane: a message sent on instance A never reaches a browser connected to instance B. Inbox realtime and tray commands will randomly fail.
- `AddMemoryCache()` used for caches that need invalidation (and the session cache from task 03).
- `IdempotentAttribute`: check where it stores keys.

## What to do

1. Add Redis (or the cloud equivalent) as a shared cache and as the SignalR backplane.
2. Move rate-limit counters, idempotency keys and the session cache to it.
3. Move web and tray presence to it with a short expiry per entry.
4. Until this task is done, write clearly in the deploy doc that UAT must run exactly one API instance, and make the load balancer sticky if a second one is ever added.
5. Add an integration test that starts two hosts and checks an inbox message reaches a client on the other host.

## Done when

- Two instances run behind a load balancer and inbox realtime, tray presence and rate limits behave the same as with one.
- The API still starts in Development without Redis (in-memory fallback).
