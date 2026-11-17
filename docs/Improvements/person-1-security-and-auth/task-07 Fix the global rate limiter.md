# Task 07: Fix the global rate limiter

| | |
|---|---|
| Priority | 7 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, Scaling |
| Depends on | nothing |

## What we found

`PublicApiRateLimitingMiddleware` allows 300 requests per minute per IP address for everything under `/api/v1`, signed in or not. A company office usually reaches the internet through one public IP. Fifty people in one office share 300 requests per minute, and one dashboard load already uses many of them. UAT users in the same room will get HTTP 429 errors.

The counter also lives in process memory, the window is fixed (easy to burst at the edge), and the limiter runs before authentication so it cannot tell users apart.

## What to do

1. Replace the hand-written middleware with the built-in ASP.NET Core rate limiter (`AddRateLimiter`).
2. Use separate partitions:
   - anonymous requests: by IP, strict;
   - signed-in requests: by session or user id, generous (start with 600 per minute);
   - tray device requests: by device id;
   - a high safety ceiling per tenant.
3. Keep the strict auth rules in `AuthRateLimitingMiddleware` as they are.
4. Put all limits in settings so UAT can tune them without a release.
5. Return `Retry-After` and the standard problem body, as today.
6. Add tests for: two users on one IP do not block each other; anonymous flood from one IP is blocked.

## Done when

- 50 simulated users on one IP can load the dashboard together without a 429.
- An anonymous flood is still blocked.
- Limits are documented in `docs/deployment/`.

## Note

Task 21 moves the counters to a shared store for multi-instance hosting.
