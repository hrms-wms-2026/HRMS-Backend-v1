# Task 44: Sensitive data in API responses

| | |
|---|---|
| Priority | 44 (lower number = build first). Added 2026-11-15. Step 1 is small and should be done before UAT opens, whatever the number says |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day (steps 1 to 4). Step 5 needs a product decision first |
| Area | Security, API |
| Depends on | - |

## What was checked

A read of the code on 2026-11-15, both repositories. Nothing was run, so no response was captured from a live server.

- Backend: every record under `DTOs/Responses`, the login and session mappers, the exception middleware, and controllers that could return an entity.
- Frontend: the auth mapper and DTOs, runtime config, `localStorage` and `sessionStorage` writes, `console` calls, hard-coded keys, source map settings.

Not checked: the tray app endpoints in depth, the platform admin frontend, file download URLs, and SignalR messages.

## What we found

### Leaks to fix

| # | API | What goes out | Who can call it | Risk |
|---|---|---|---|---|
| 1 | `GET /api/v1/auth/invitations/{token}` | `tenant_id` (internal tenant Guid) and `invitation_id` | Anyone with the email link. No login | Medium. Login hides the tenant id on purpose (`[JsonIgnore]` on `CurrentUserDto`); this endpoint gives it away |
| 2 | `POST /api/v1/attendance/corrections/preview`, `POST /api/v1/attendance/corrections`, `GET .../my`, `GET .../approvals`, and approve, reject, cancel | `approver.userId` (internal login user Guid of the approver), next to `employeeId` | Any signed-in employee who files or reviews a correction | Low. Same tenant only, but the user id is the login identity and the app works on employee ids |
| 3 | `POST /api/v1/attendance/work-area-change-requests/preview`, `POST` (create), `GET .../my`, `GET .../approvals`, and approve, reject, cancel | `receiver.userId`, same as row 2 | Any signed-in employee | Low |
| 4 | `GET /api/v1/employees/{id}/detail` | Date of birth, phone, home addresses, emergency contacts (name, phone, email) | Anyone with `employees:read` whose coverage includes the employee, for example a reporting manager | Product decision. Bank data on the same response is behind `employees:read:sensitive`; personal data is not |

Evidence:

- Row 1: `InvitationDetailDto` and `InviteMapper.ToDto`. The frontend type `InvitationPreviewDto` declares `tenant_id` and `invitation_id`, and no frontend code reads either.
- Rows 2 and 3: `AttendanceCorrectionApproverResponse` and `WorkAreaChangeApproverResponse`, filled from `route.ApproverUserId` in the two workflow classes. The frontend models carry `userId`; a search found no code that reads it.
- Row 4: `GetEmployeeDetailQueryHandler`. Payroll is built only inside `HasPermission("employees:read:sensitive")`; `EmployeeDetailPersonalInformation` and the emergency contacts are built for every caller who passes the visibility check.

### Misleading names (not a leak, but they hide real ones)

| API | Field | What it really holds |
|---|---|---|
| `GET /api/v1/work/projects`, `GET /api/v1/work/projects/mine`, `GET /api/v1/work/projects/{id}` | `members[].userId` | An employee id. `ProjectMemberAvatarResolver.BuildAvatars` passes `employeeId` into `ProjectMemberAvatarDto.UserId` |
| `POST /api/v1/work/projects` | `creatorMembership.userId` | Not confirmed. Check what `CreateProjectCommandHandler` puts in `ProjectMembershipSummaryDto.UserId` |

A field called `userId` that holds an employee id makes every later review harder, because a reviewer cannot tell a real user id from a renamed one.

### Frontend items

| Where | Finding |
|---|---|
| `core/auth/feature/login/login.component.ts` line 89 | `console.log('Initiating Google SSO login...')` in an empty `signInWithGoogle()`. No data in it, but it is a leftover |
| `angular.json`, `staging` configuration | `sourceMap: true`. If the staging site is reachable from outside, the full TypeScript source is readable. The production configuration does not set it |

### What is already safe (do not redo)

- Login and session responses carry email, permissions, active modules and the workspace slug only. CSRF token, MFA challenge and legal challenge travel as cookies. `UserId` and `TenantId` are `[JsonIgnore]`.
- Bank account numbers are stored encrypted and returned masked. The full number needs `POST /api/v1/employees/{id}/bank-details/reveal`, which has `employees:read:sensitive`, sends `Cache-Control: no-store`, and writes an audit record.
- An unhandled exception returns "An unexpected error occurred." and a correlation id. No exception message or stack trace.
- No controller returns a domain entity.
- Integration credentials return `HasAccessToken` and `HasRefreshToken` as booleans, never the token.
- The browser stores only display preferences (theme, menu state, view mode, selected legal entity id). No token and no user data.
- No API key or secret is hard-coded in the frontend.

## What to do

1. **Invitation preview (row 1).** Remove `InvitationId` and `TenantId` from `InvitationDetailDto` and from `InviteMapper.ToDto`. Remove `invitation_id` and `tenant_id` from the frontend `InvitationPreviewDto`. Check the accept endpoints still work; they take the token from the URL, not these ids.
2. **Attendance approver (rows 2 and 3).** Remove `UserId` from `AttendanceCorrectionApproverResponse` and `WorkAreaChangeApproverResponse`. Keep `EmployeeId`. Remove `userId` from the two frontend models. Before removing, search the frontend once more for any read of `approver.userId` or `receiver.userId`.
3. **Project members.** Rename `ProjectMemberAvatarDto.UserId` to `EmployeeId` in the DTO, the view model and the frontend `project.dto.ts` and `project.model.ts`. Confirm what `ProjectMembershipSummaryDto.UserId` holds and rename or remove it the same way. This is Work Management code, so agree it with the Work Management owner.
4. **Frontend clean-up.** Delete the `console.log` and wire or remove the empty `signInWithGoogle()`. Set `sourceMap` to `false` for `staging`, or set `"sourceMap": { "scripts": true, "hidden": true }` and keep the map files off the public host.
5. **Employee personal data (row 4).** Ask the product owner one question: should a reporting manager see a team member's date of birth, home address and emergency contacts? If no, build the personal section and emergency contacts only when the caller has `employees:read:sensitive` or is HR, the same way payroll is built today, and return `null` otherwise so the screen still loads. Add the new rule to the permission catalogue (task 32).

## How to keep it from coming back

These three guards make the check automatic. Build them in this task if time allows, otherwise as a follow-up.

1. **Architecture test on response DTO field names.** In `ONEVO.Tests.Architecture`, reflect over every type in a `DTOs.Responses` namespace and every `*ViewModel`. Fail when a property name matches `TenantId`, `UserId`, `Password`, `Hash`, `Salt`, `Secret`, `Token`, `ApiKey`, `ConnectionString` or `Encrypted`, unless the property has `[JsonIgnore]` or the type is in a short allow-list with a written reason beside each entry. Start the allow-list with what exists today: `MfaSetupDto.Secret` (the user must see it to set up MFA), `TrayAuthResponseDto` (tray app tokens), `EnrollmentAttemptResponse` (short-lived AWS credentials for the tray app), admin `Tenant*` DTOs (platform admins work on tenants by id), `LoginResponseDto` and `AdminLoginResultDto` (internal, never serialized).
2. **Anonymous endpoint snapshot test.** For each `[AllowAnonymous]` action, an integration test asserts the exact set of JSON field names in the response. A new field on a public endpoint then needs a deliberate test change.
3. **Data classes written down.** Add `docs/security/data-classification.md` with three classes and the rule for each:

   | Class | Examples | Rule |
   |---|---|---|
   | Secret | Password hash, session token, CSRF token, MFA secret, OAuth tokens, encryption keys | Never in a response body. Cookies or server only |
   | Internal id | `TenantId`, `UserId`, session id | Not sent to the tenant app. Use slug and `EmployeeId` |
   | Personal, restricted | Bank account, date of birth, home address, emergency contacts, salary | Sent only with the matching permission. Bank account masked by default, full value through an audited reveal |

4. **Review checklist line.** Add to the pull request template: "New or changed response DTO: no secret, no internal id, restricted personal data is behind a permission."
5. **Scan.** `pwsh ../dev-env/scripts/security-scan.ps1 backend -Baseline origin/development` already runs Semgrep. Add a rule that flags a new property named as in guard 1 inside `DTOs/Responses`.

## Done when

- `GET /api/v1/auth/invitations/{token}` returns no `tenant_id` and no `invitation_id`, and invitation accept still works for password, Google and employee flows.
- No attendance correction or work area change response contains `userId`.
- Project member fields are named for what they hold.
- The architecture test from guard 1 passes, and fails when a `TenantId` property is added to a tenant response DTO.
- The staging build does not publish source maps, and `npm run lint` and `npm run check:warnings` are clean.
- The product owner's answer for row 4 is written in this file, and the code matches it.
