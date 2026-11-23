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

## Results (2026-11-19)

Branch `claude/improve-task-44-sensitive-data-c98209`, started from `origin/development` at 846b92028.

### Done

| Step | What changed |
|---|---|
| 1. Invitation preview | `InvitationDetailDto` and `InviteMapper.ToDto` no longer carry `InvitationId` or `TenantId`. `GET /api/v1/auth/invitations/{token}` returns no `invitation_id` and no `tenant_id`. The accept endpoints (password, Google, employee) never used this DTO; they work from the token in the URL, and the tenant provisioning E2E test still accepts through it |
| 2. Attendance approver | `AttendanceCorrectionApproverResponse` and `WorkAreaChangeApproverResponse` no longer carry `UserId`; `EmployeeId` stays. The "correction created" notification now takes the approver's user id from the approval route instead of the response object |
| Guard 1 | `tests/ONEVO.Tests.Architecture/Security/ResponseDtoSensitiveFieldArchitectureTests.cs`. Scans every `DTOs.Responses` type and every API `*ViewModel`. Internal-id terms match a name ending in `TenantId` or `UserId`; secret terms match only properties that can hold a value (not flags, counts or dates, so `HasAccessToken` and `TokenExpiresAt` pass). A probe record proves an added `TenantId` fails. A second test fails on stale allow-list entries |
| Guard 2 | The invitation preview field set is pinned twice: in the architecture test (from the DTO) and in `TenantProvisioningE2ETests` (from the live JSON). The work-area integration test now asserts `receiver.employeeId` and the absence of `receiver.userId` |
| Guard 3 | `docs/security/data-classification.md`, linked from `docs/README.md` |
| Guard 4 | `.github/pull_request_template.md` (there was no template) with the response-DTO line, plus the RLS and permission lines from `CLAUDE.md` |

### Open decisions (not changed)

1. **Row 4, employee personal data.** *Decided 2026-11-23: yes, managers can see them. No code change; see the follow-up section below.* Needed the product owner's answer: should a reporting manager see a team member's date of birth, home address and emergency contacts? Code is unchanged until then. If the answer is no, gate the personal section and emergency contacts in `GetEmployeeDetailQueryHandler` the way payroll is gated today, and add the rule to the permission catalogue (task 32).
2. **Step 3, project members.** *Done 2026-11-23 (backend and frontend together); see the follow-up section below.* Needed the Work Management owner. `ProjectMemberAvatarDto.UserId` holds an employee id, and the frontend reads `member.userId` in six places (project card, project list, milestone tree, task backlog, task board, task form), so the rename must ship together with the frontend. `ProjectMembershipSummaryDto.UserId` (`creatorMembership.userId` on `POST /api/v1/work/projects`) is confirmed to hold the creator's **login user id**, a real internal id. The frontend does not read it, but `CreateProjectEndpointTests` uses it to find the owner. Suggested fix: replace it with `EmployeeId` (`member.EmployeeId`). Both are `FOLLOW-UP` entries in the guard's allow-list.
3. **Guard 5, Semgrep rule.** Not added. The rule set lives in `../dev-env` (outside this repo), and the architecture test covers the same names more precisely. Add a Semgrep rule only if the scan must also catch it outside CI.

### More leaks found by the guard (fixed 2026-11-23, see the follow-up section)

| Response | Field | Endpoint |
|---|---|---|
| `FileRecordDto` | `TenantId`, `UploadedByUserId` | `GET/POST /api/v1/files…`, leave document upload, tray screenshot upload. The tray client also reads this DTO, so check it before removing anything |
| `OnboardingAccessGrantRequestListItemResponse` | `RequestedByUserId`, `DecidedByUserId` | Onboarding access request list |
| `GitHubTenantApprovalDto` | `EnabledByUserId` | Tenant GitHub integration approval |

The guard does not catch audit fields named `…ById`. `AttendanceCorrectionResponse.RequestedById` / `ReviewedById` and `WorkAreaChangeRequestResponse.ReviewedById` are login user ids. The frontend models declare them, but only spec files read them. Changing them is part of the same follow-up.

### Frontend items (for the frontend team; the frontend repo was not edited)

1. `core/auth/models/dto/invitation-preview.dto.ts`: delete `invitation_id` and `tenant_id`. No code reads them.
2. `modules/attendance/models/attendance-correction.model.ts` line 24 and `work-area-change-request.model.ts` line 13: delete `userId` from the approver / receiver model. No code reads them.
3. `core/auth/feature/login/login.component.ts` line 89: delete the `console.log` and wire or remove the empty `signInWithGoogle()`.
4. `angular.json`, `staging`: set `sourceMap` to `false`, or use `{ "scripts": true, "hidden": true }` and keep map files off the public host.
5. When step 3 is agreed: rename `userId` to `employeeId` in `modules/work/models/dto/project.dto.ts` and `project.model.ts`, and in the six components that read `member.userId`.

### Verification

- `find-unauthorized-endpoints.ps1` on the worktree: 0 actions without an authorization attribute; 32 explicit `[AllowAnonymous]` actions, none added.
- Full gate (`verify.ps1 <worktree> -Full`, report `dev-env/reports/improve-task-44-sensitive-data-c98209/verify-20261119-151802`): build PASS, unit 8019 passed, architecture 720 passed, integration 1251 passed / 1 skipped, 0 failed. No flaky re-runs needed.

## Status (2026-11-23)

Steps 1, 2 and 4 done. Merged to `development` in PR #240 on 2026-11-20.

Decisions 2026-11-23:

- **Row 4, employee personal data: accepted as it is.** A reporting manager may see a team member's date of birth, home address and emergency contacts. No code change.
- **Step 3, project members: approved.** `ProjectMemberAvatarDto.UserId` becomes `EmployeeId` in the backend and the frontend together (frontend item 5 above). Next person-1 follow-up.
- **The extra `...ById` audit fields** (`AttendanceCorrectionResponse.RequestedById` and `ReviewedById`, `WorkAreaChangeRequestResponse.ReviewedById`): fixed as part of the same follow-up.

## Follow-up 44b (2026-11-23)

Backend branch `claude/improve-task-44b-employee-ids` (from `origin/development` b6fabb74f). Frontend branch `claude/improve-task-44b-employee-ids` in the frontend repo.

Decisions from the user:

- **Row 4:** "Yes, managers can see them." A reporting manager with `employees:read` whose coverage includes the employee keeps seeing date of birth, phone, home addresses and emergency contacts on `GET /api/v1/employees/{id}/detail`. Bank data stays behind `employees:read:sensitive`. No code change.
- **Step 3:** backend and frontend change together.

| Response | Change |
|---|---|
| `ProjectMemberAvatarDto` / `ProjectMemberAvatarViewModel` (`members[]` on `GET /api/v1/work/projects`, `/mine`, `/{id}`) | `userId` renamed to `employeeId`. The value is unchanged: it was always the employee id |
| `ProjectMembershipSummaryDto` / `ProjectMembershipViewModel` (`creatorMembership` on `POST /api/v1/work/projects`) | `userId` (the creator's login user id) replaced by `employeeId` (the membership's employee id) |
| `FileRecordDto` (`/api/v1/files…`, leave document upload, tray screenshot upload) | `tenantId` and `uploadedByUserId` are `[JsonIgnore]`. They stay on the record because about ten ownership checks (only the uploader may link, read or delete a pending file) use them. The tray app was checked first: it reads its tenant id from its own JWT and parses the screenshot upload response only for `stored`, `duplicates` and `rejected` |
| `OnboardingAccessGrantRequestListItemResponse` (onboarding access request list) | `requestedByUserId` and `decidedByUserId` removed. `requestedByName` and `decidedByName` stay |
| `GitHubTenantApprovalDto` (tenant GitHub approval) | `enabledByUserId` removed |

The guard's seven `FOLLOW-UP` allow-list entries are removed, so the guard now has no known tenant-app leak on its list. The OpenAPI files did not change: they do not describe these response bodies, and regeneration gave a line-ending-only diff.

Still open (not part of this follow-up): audit fields named `…ById` (`RequestedById`, `ReviewedById` on the attendance responses) are login user ids that the guard does not catch. The onboarding inbox builds an `InboxPerson` from `DecidedByUserId` when no employee id is known.
