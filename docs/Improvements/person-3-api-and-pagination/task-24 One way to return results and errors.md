# Task 24: One way to return results and errors

| | |
|---|---|
| Priority | 24 of 30 (lower number = build first) |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day |
| Area | API, Code quality |
| Depends on | nothing |

## What we found

- The same `ToResult` and `ToNoContent` helper methods are written again in at least six controllers (`ConversationsController`, `InboxChatsController`, `InboxController`, `ProjectMonitorController`, `MonitoringActivationController`, `MonitoringDeviceAuthorizationController`).
- The 401 and 403 JSON bodies are written by hand four times in `AuthenticationExtensions` (the duplicate scan reports a 31-line copy), again in `PermissionVersionMiddleware`, in both rate limiters and in `ExceptionHandlerMiddleware`. The shapes are close but not identical: some include `correlationId` and `instance`, some do not.
- Handlers use two styles: some return `Result<T>`, others throw `NotFoundException`, `DomainException` or `ForbiddenException`.

The frontend therefore has to handle several error shapes.

## What to do

1. Create one `ProblemResponseWriter` in the API project that writes the problem body with type, title, status, detail, instance, errors and correlationId. Use it in every middleware, both cookie schemes and the JWT scheme.
2. Create an `ApiControllerBase` with the shared `ToResult` methods and remove the copies.
3. Write the rule in `docs/api/errors.md`: which HTTP status means what, the exact body, and when a handler returns `Result` versus throws.
4. Add a stable machine-readable `code` field for errors the frontend must react to.
5. Do not change status codes in this task. Only the body becomes uniform.

## Done when

- A search for `WriteAsJsonAsync(new` in the API project finds only the shared writer.
- Every error response has the same fields.
- The error doc is shared with the frontend (frontend task 20).
