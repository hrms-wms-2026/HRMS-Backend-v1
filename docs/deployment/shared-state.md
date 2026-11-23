# Shared state for more than one instance

Task 21 (`docs/Improvements/person-4-deployment-and-architecture/task-21 Shared state for running more than one instance.md`),
built as **21a**. What two or more API instances must share, how it is shared, and what still needs 21b.

## The switch

`Redis__ConnectionString` (a secret, for example
`<name>.redis.cache.windows.net:6380,password=<key>,ssl=True,abortConnect=False`).

- **Set:** the pieces below use Redis.
- **Not set:** each piece keeps its in-process behaviour, exactly as before task 21. Development and a single UAT
  instance need nothing. No Redis code runs and nothing tries to connect.

A configured but unreachable Redis does not stop start-up. Every piece has a fallback (below), and `/health/ready`
reports **Degraded** (still 200) for Redis, for the same reason as storage and the outbox.

Code: `src/ONEVO.Infrastructure/SharedState/` and `src/ONEVO.Api/Extensions/SharedStateExtensions.cs`.

## What is shared

| State | Before | With Redis | If Redis fails |
|---|---|---|---|
| Sign-in cookie key ring (data protection) | in memory, lost on every restart | Redis key `crownus:data-protection-keys`; application name fixed to `crownus-api` | the multiplexer reconnects; protecting needs the ring, so sign-in waits for Redis |
| Session ticket cache (task 03) | evictions only on the instance that made the change | each instance keeps its local cache; evictions (session, user, tenant, all) go to every instance over pub/sub `crownus:invalidate` | evictions reach the others only after the ticket cache lifetime (30 s). On reconnect every instance evicts everything |
| Tenant-by-slug cache, `ICacheService` | per instance | removals go over the same bus | as above |
| Permission versions | per instance, reset on restart | one counter per user, `crownus:perm-ver:<user>` | the permission check fails open, as it always has (it only applies to non-browser tokens; browser sessions are rebuilt from the database) |
| Admin MFA challenges | per instance: the code step failed if it reached another instance | `crownus:admin-mfa:<hash>` hash, expiring with the challenge; failed attempts and consume are atomic scripts | sign-in for admins with MFA fails until Redis is back |
| Sign-in / MFA / reset limits (`AuthRateLimitingMiddleware`) | per instance | one counter per rule and hashed identity, `crownus:attempts:...` (INCR + expiry in one script) | counted on the instance instead: the limit becomes per-instance, never removed |
| Tray presence | count per instance | one sorted-set member per hub connection, scored with its expiry (90 s), refreshed every 30 s by the instance that holds the connection; a crashed instance's entries drop out by themselves | "not connected here", so `TrayPresenceEnforcementMiddleware` falls back to the device's persisted HTTP heartbeat (as before) |
| Web presence | per instance | last request time per user, written at most every 10 s per user | the local time |
| SignalR (inbox live updates, tray commands) | a message reached only clients on the sending instance | Redis backplane, channel prefix `crownus:signalr` | messages reach only local clients |

Idempotency keys were already in SQL Server (`EfIdempotencyStore`) and needed nothing.

## Settings and Redis requirements

| Variable | Secret | Notes |
|---|---|---|
| `Redis__ConnectionString` | yes | Turns all of the above on |
| `DataProtection__KeyEncryptionCertificate` | yes | Base64 PFX. Encrypts the key ring at rest in Redis. Without it the API logs a warning at start |
| `DataProtection__KeyEncryptionCertificatePassword` | yes | The PFX password |

Redis itself:

- **TLS and an access key**, reachable only from the API (private endpoint or firewall). Anyone who can read Redis
  can read the key ring unless it is encrypted with the certificate above.
- **Eviction policy `volatile-lru` (the Azure default) or `noeviction`, never `allkeys-*`.** The key ring and the
  permission counters have no expiry; an `allkeys-*` policy could evict them under memory pressure, which signs
  everyone out or forgets permission changes.
- A Redis restart without persistence loses the key ring (everyone signs in again) and the permission counters (as a
  process restart does today). Standard tier or above replicates, so a failover keeps them.

## Load balancer

**Keep session affinity (App Service "ARR affinity") on** when running more than one instance. Neither the web app
nor the tray skips SignalR negotiation, and negotiation plus the connection must reach the same instance. The
backplane does not remove that need; it makes messages reach a client whichever instance sent them.

## Not done yet (21b and later)

- **The global API rate limiter** (`ApiRateLimitingExtensions`, task 07) still counts per instance, so the effective
  limit is the configured limit times the number of instances. Person 2's tasks 45 to 49 are rewriting that limiter
  to read its limits from the database; a Redis version follows after them (task 21b).
- `SendTestNotificationCommandHandler` uses `ICacheService` as a per-hour counter, which stays per instance. Minor.
- **Not tested end to end:** a SignalR message sent on one host reaching a client connected to another. The tests
  check that both hosts use the Redis backplane, not a live client.
- **A separate worker instance** (background jobs off on web instances, task 13) also needs the backplane, because
  the outbox sends inbox and tray messages. Split only once Redis is configured on UAT.
- UAT has no Redis yet. The subscription owner has to create an Azure Cache for Redis (Basic C0 is enough for UAT;
  Standard for failover) and give you its connection string.

## Tests

| What | Where |
|---|---|
| Two Redis connections as two instances: session/user/tenant evictions, permission versions, MFA (shared, single consume under a race, attempt limit, expiry), attempt counter (shared, window, Redis-down fallback), tray presence (shared, disconnect, dead instance drops out, refresh keeps alive, Redis down = not connected), web presence, data protection with and without key encryption | `tests/ONEVO.Tests.Integration/SharedState/RedisSharedStateTests.cs` |
| Two real API hosts, one database, one Redis: ready, cookie keys shared, user eviction reaches the other host, tray presence, Redis backplane on both | `tests/ONEVO.Tests.Integration/SharedState/TwoInstanceApiTests.cs` |
| No Redis setting: Development and single-instance behaviour unchanged | the existing unit and `ApiBootTests` suites |

Redis for the tests: Testcontainers, pinned to `redis:7.4-alpine`.
