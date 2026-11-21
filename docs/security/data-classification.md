# Data classification for API responses

Every field an API returns falls into one of three classes. The class decides whether it may leave the server and under what rule. Added by improvement task 44 (sensitive data in API responses).

| Class | Examples | Rule |
|---|---|---|
| Secret | Password hash, session token, CSRF token, MFA secret, OAuth tokens, encryption keys | Never in a response body. Cookies or server only. Return a flag such as `HasAccessToken` when the client needs to know a secret exists |
| Internal id | `TenantId`, `UserId` (the login identity), session id | Not sent to the tenant app. Use the workspace slug and `EmployeeId`. The platform admin app may use tenant and platform user ids, because admins work on tenants by id |
| Personal, restricted | Bank account, date of birth, home address, emergency contacts, salary | Sent only with the matching permission. Bank account is masked by default; the full value comes only through an audited reveal (`POST /api/v1/employees/{id}/bank-details/reveal`, `employees:read:sensitive`, `Cache-Control: no-store`) |

## Rules for new code

- A field called `UserId` must hold a login user id. A field that holds an employee id is called `EmployeeId`. A misnamed field hides real leaks from review.
- An anonymous (`[AllowAnonymous]`) endpoint returns only what the caller needs to finish the flow. Anyone holding the link or token can read it.
- A response DTO that the controller never serializes (for example a handler result mapped to cookies) marks its secret fields `[JsonIgnore]`, so a future `Ok(result.Value)` cannot leak them.

## How it is enforced

- `tests/ONEVO.Tests.Architecture/Security/ResponseDtoSensitiveFieldArchitectureTests.cs` reflects over every type in a `DTOs.Responses` namespace and every `*ViewModel` in the API. A property whose name ends with `TenantId` or `UserId`, or contains `Password`, `Hash`, `Salt`, `Secret`, `Token`, `ApiKey`, `ConnectionString` or `Encrypted` (secret terms only for string-like values, not flags, counts or dates), fails the test unless it is `[JsonIgnore]` or on the allow-list with a written reason. Entries marked `FOLLOW-UP` are known tenant-app leaks waiting for a fix; remove the entry when the fix lands and do not add new ones.
- Anonymous endpoint snapshots: the invitation preview (`GET /api/v1/auth/invitations/{token}`) has its exact JSON field set asserted in `TenantProvisioningE2ETests` and in the architecture test above. A new field there needs a deliberate test change.
- Pull request template: "New or changed response DTO: no secret, no internal id, restricted personal data is behind a permission."
