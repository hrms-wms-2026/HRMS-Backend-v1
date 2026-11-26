# Owner Self-Employee Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** an account without an employee profile sees a calm home screen. The owner can add themselves through the existing Add Employee wizard, which creates the employee on their own login, activates the seat at once and sends no invitation.

**Architecture:**
- **Backend:** `OnboardingDraftWriteService.FinalizeAsync` gains a *self* branch, taken when the draft's work email is the acting user's own login email. A new query and endpoint tell the web whether "add yourself" is possible.
- **Web:** the dashboard shell shows a no-profile panel on My Work when `AuthStore.employeeId()` is null; the panel hosts the wizard in self mode. The error interceptor stops toasting the "No employee record" message.

**Tech Stack:** .NET 10, MediatR, EF Core on SQL Server, xUnit, Moq and FluentAssertions (backend). Angular 21 signals, NgRx signal store and Vitest (web).

**Spec:** `docs/superpowers/specs/next/2026-11-24-owner-self-employee-design.md`

**Repos and branches:**
- **Backend:** `C:\onevoNew\HRMS-Backend-v1-singlehost`, branch `feat/owner-self-employee`, which already holds the spec.
- **Web:** `C:\onevoNew\Hrms--Web-application---front-end---v1-singlehost`, new branch `feat/owner-self-employee` from `origin/Development` (capital D).

## Global Constraints

- Finalizing someone else's draft must behave exactly as today: planned seat, `InvitationToken`, `EmployeeOnboardingInviteEmail` outbox message, `MessageKey "onboarding.finalize.invitation_sent"`.
- No new wizard fields. All existing finalize validation stays.
- A self draft is identified **only** by: the normalized draft `WorkEmail` equals the normalized `ICurrentUser.Email` **and** the user found by email has `Id == actingUserId`.
- The endpoint permission is `PermissionCatalog.Employees.Write`, the same as the other onboarding lookups.
- Exact copy:
  - Panel title: "Your employee profile isn't set up yet"
  - Ready text: "Add yourself as an employee to see your work here."
  - Button: "Add yourself as an employee"
  - Not-ready text: "Add a legal entity and a position first."
  - Success: "You're set up as an employee"
- The toast is suppressed only for the exact detail `No employee record for the current user.`
- **Backend gate:** `dotnet build src/ONEVO.Api/ONEVO.Api.csproj -c Release`, plus unit and architecture tests green.
- **Web gate:** `npm run check:warnings` clean, touched specs green, `npx eslint <changed files>` clean.

## Review Focus

1. **A self draft whose position template requires approval and the owner can't self-authorize.** It falls into today's waiting-for-approval path. Expected: unchanged behaviour; the invite goes out on approval. Task 1 asserts that the self branch is not taken on the approval path.
2. **The owner types a different email in the wizard.** The email field is locked in self mode, but the API can still receive another email. Expected: a normal draft with an invitation. Task 1 test: `Finalize_OtherEmail_SendsInvitation`.
3. **The email matches but `ICurrentUser.Email` differs only in case or spaces.** Expected: still treated as self. Task 1 test uses `" Owner@Acme.Test "`.
4. **The dashboard loads before `checkSession()` resolves `employeeId`.** Expected: no flash of the panel for real employees. Task 5 test: the panel renders only when the session is authenticated **and** `employeeId()` is null.
5. **The status endpoint fails (403 for a user without employees:write).** Expected: the panel still shows the title, without the button. Task 5 test.

---

### Task 1: Self branch in finalize (backend)

**Files:**
- Modify: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs`: the finalize path around the seat reservation (`TryReservePositionAssignmentAsync`) and the invitation and outbox block (`NewInvitation`, `EnqueueAsync(OutboxMessageTypes.EmployeeOnboardingInviteEmail…)`), plus the success return (`MessageKey: "onboarding.finalize.invitation_sent"`).
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/OnboardingDraftWriteServiceTests.cs`

**Interfaces:**
- Consumes:
  - `ICurrentUser.Email`, already injected as `_currentUser`;
  - `IPositionAssignmentRepository.ActivatePlannedAsync(Guid tenantId, Guid positionAssignmentId, CancellationToken)`;
  - `IUserRepository.GetByTenantAndEmailAsync`.
- Produces: `FinalizeOnboardingDraftResponse` with `InvitationQueued: false`, `PositionApprovalPending: false`, `MessageKey: "onboarding.finalize.self_employee_created"` for self drafts. Task 4 reads this `MessageKey`.

- [ ] **Step 1: Write the failing tests**, using the file's existing builders and mocks:
  - `Finalize_SelfDraft_ActivatesSeatAndSendsNoInvitation`:
    - draft `WorkEmail = " Owner@Acme.Test "`, `_currentUser.Email = "owner@acme.test"`, the user repo returns a user with `Id == actingUserId`, the seat reservation returns `reservedId`;
    - assert `ActivatePlannedAsync(tenantId, reservedId, …)` is called once;
    - assert `IInvitationTokenRepository.AddAsync` is never called;
    - assert the outbox `EnqueueAsync` is never called with `OutboxMessageTypes.EmployeeOnboardingInviteEmail`;
    - assert `result.Value.InvitationQueued == false` and `result.Value.MessageKey == "onboarding.finalize.self_employee_created"`.
  - `Finalize_OtherEmail_SendsInvitation`: the draft email differs from the current user's; assert the invitation is added, the outbox is enqueued, `ActivatePlannedAsync` is never called, and `MessageKey == "onboarding.finalize.invitation_sent"`.
  - `Finalize_SelfEmail_ButFoundUserIsSomeoneElse_IsConflict`: the emails match but the found user's `Id != actingUserId`; assert `StatusCode == 409` and nothing is enqueued.
  - `Finalize_SelfDraft_WaitingForPositionApproval_IsUnchanged`: the template requires approval and the actor can't self-authorize; assert `MessageKey == "onboarding.finalize.waiting_for_position_approval"`.

- [ ] **Step 2: Run them and see them fail**

  Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~OnboardingDraftWriteServiceTests"`

  Expected: the first and third tests fail; the second and fourth pass already.

- [ ] **Step 3: Implement the self branch**
  - Compute `isSelf` once, after the user is resolved: compare the normalized emails; then, if the emails match and `user.Id != actingUserId`, return `Conflict("This email belongs to a different sign-in in this company.")`.
  - Inside the existing transaction, when `isSelf`:
    - after the reservation succeeds, call `ActivatePlannedAsync(draft.TenantId, reservedAssignmentId.Value, txnCt)`;
    - skip the `NewInvitation`, `AddAsync` and `EnqueueAsync` block.
  - Return `InvitationQueued: false`, `MessageKey: "onboarding.finalize.self_employee_created"`. Keep `ChecklistTasksCreated` as computed today.
  - The approval-pending early return stays before the self logic and is untouched.

- [ ] **Step 4: Run the tests and see them pass**

  Same command. Expected: all `OnboardingDraftWriteServiceTests` pass.

- [ ] **Step 5: Commit**

  ```bash
  git add src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Services/OnboardingDraftWriteService.cs tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/OnboardingDraftWriteServiceTests.cs
  git commit -m "feat(onboarding): finalizing your own draft activates the seat and sends no invitation"
  ```

### Task 2: Self-setup status endpoint (backend)

**Files:**
- Create: `src/ONEVO.Application/Features/CoreHr/OnboardingDraft/Queries/GetSelfEmployeeSetupStatus/GetSelfEmployeeSetupStatusQuery.cs`, which holds the query, the handler and the response record. Follow the sibling `GetOnboardingWizardLookups` folder for namespace and file split.
- Modify: `src/ONEVO.Api/Controllers/Tenant/CoreHr/OnboardingDraftsController.cs`: add `[HttpGet("self-setup")]` next to `clock-in-defaults`.
- Test: `tests/ONEVO.Tests.Unit/Features/CoreHr/OnboardingDraft/GetSelfEmployeeSetupStatusQueryHandlerTests.cs`

**Interfaces:**
- Consumes:
  - `ICurrentUser.UserId`;
  - `ITenantContext.TenantId`;
  - `IEmployeeRepository.GetByUserIdAsync(tenantId, userId, ct)`;
  - `IUserRepository.GetByIdAsync(userId, ct)`;
  - `ILegalEntityRepository` and `IPositionRepository`, through whichever existing "any active" or list method fits; add a minimal `AnyActiveAsync` if none exists.
- Produces:
  - `GET /api/v1/onboarding/drafts/self-setup` returns `SelfEmployeeSetupStatusResponse(bool HasEmployeeProfile, bool HasLegalEntity, bool HasPosition, bool CanAddSelf, string FirstName, string LastName, string WorkEmail)`.
  - JSON names are snake_case, like the rest of the API: `has_employee_profile`, `has_legal_entity`, `has_position`, `can_add_self`, `first_name`, `last_name`, `work_email`.
  - `CanAddSelf = !HasEmployeeProfile && HasLegalEntity && HasPosition`.
  - `HasPosition` means at least one active position that has a department. Seat capacity is still enforced by finalize, so the wizard shows the capacity error if every seat is full.

- [ ] **Step 1: Write the failing tests:**
  - `HasProfile_CannotAddSelf`;
  - `NoLegalEntity_CannotAddSelf`;
  - `NoPosition_CannotAddSelf`;
  - `Ready_CanAddSelf_AndReturnsLoginNameAndEmail`: the user has `FirstName "Ada"`, `LastName "Owner"` and `Email "ada@acme.test"`; assert those three fields and `CanAddSelf == true`.
- [ ] **Step 2: Run and see them fail.** Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj -c Release --filter "FullyQualifiedName~GetSelfEmployeeSetupStatus"`. Expected: compile failure or FAIL.
- [ ] **Step 3: Implement the query and handler, and the endpoint** with `[RequirePermission(PermissionCatalog.Employees.Write)]`, returning `Ok(result.Value)` or `Problem(...)` like its neighbours.
- [ ] **Step 4: Run the tests and see them pass.** Then run the architecture tests (`dotnet test tests/ONEVO.Tests.Architecture/ONEVO.Tests.Architecture.csproj -c Release`). Expected: all green. The endpoint is authorized, so the unprotected-endpoint scan stays at 0.
- [ ] **Step 5: Commit:** `feat(onboarding): self-setup status for an account without an employee profile`

### Task 3: Don't toast the no-employee message (web)

**Files:**
- Modify: `src/app/core/interceptors/error.interceptor.ts`: before `errorHandler.handle(error)`.
- Test: `src/app/core/interceptors/error.interceptor.spec.ts`

**Interfaces:**
- Produces: `export const NO_EMPLOYEE_DETAIL = 'No employee record for the current user.';` in the interceptor file. Task 5 imports it.

- [ ] **Step 1: Write the failing specs:**
  - `does not show an error for the no-employee detail`: a 403 with `error.detail === NO_EMPLOYEE_DETAIL` means `ErrorHandlerService.handle` is not called;
  - same for a 404 with that detail;
  - `still shows other 403s`: a 403 with another detail calls `handle`.
- [ ] **Step 2: Run and see them fail.** Run: `npx ng test --watch=false --include "src/app/core/interceptors/error.interceptor.spec.ts"`.
- [ ] **Step 3: Implement.** Skip `errorHandler.handle` when `error.error?.detail === NO_EMPLOYEE_DETAIL`. The error still rethrows, so callers can show their own state.
- [ ] **Step 4: Run and see them pass.**
- [ ] **Step 5: Commit:** `fix(errors): no toast for the no-employee-profile response`

### Task 4: Wizard self mode (web)

**Files:**
- Modify: `src/app/modules/people/feature/add-employee-wizard/add-employee-wizard.component.ts` and its template.
- Test: `src/app/modules/people/feature/add-employee-wizard/add-employee-wizard.component.spec.ts`

**Interfaces:**
- Consumes: Task 1's `MessageKey "onboarding.finalize.self_employee_created"`, from the finalize response the wizard store already receives.
- Produces:
  - new input `readonly selfPrefill = input<{ firstName: string; lastName: string; workEmail: string } | null>(null);`
  - the existing `saved` output now emits `{ finalized: true, draftId, self: true }` for a self finalize. Widen the output type to `{ finalized: boolean; draftId?: string; self?: boolean }`.

- [ ] **Step 1: Write the failing specs:**
  - `prefills names and email from selfPrefill and disables workEmail`;
  - `shows "You're set up as an employee" after a self finalize`: the store's finalize response has the self `MessageKey`;
  - `emits saved with self: true`.
- [ ] **Step 2: Run and see them fail.** Run: `npx ng test --watch=false --include "src/app/modules/people/feature/add-employee-wizard/*.spec.ts"`.
- [ ] **Step 3: Implement.**
  - When `selfPrefill()` is set on open: patch `firstName`, `lastName` and `workEmail`, then `form.controls.workEmail.disable()`.
  - Read `workEmail` with `getRawValue()` wherever the draft is saved, so the disabled value is still sent.
  - Choose the success copy from the finalize `MessageKey`.
- [ ] **Step 4: Run and see them pass.** Also run the existing wizard specs unchanged.
- [ ] **Step 5: Commit:** `feat(people): Add Employee wizard self mode`

### Task 5: No-profile panel on My Work (web)

**Files:**
- Create: `src/app/modules/dashboard/feature/no-employee-profile-panel/no-employee-profile-panel.component.ts`, a standalone OnPush component with an inline template.
- Create: `src/app/modules/dashboard/data-access/self-setup-api.service.ts`
- Create: `src/app/modules/dashboard/models/self-setup-status.model.ts`
- Modify: `src/app/modules/dashboard/feature/dashboard-shell/dashboard-shell.component.ts`
- Tests: the two spec files next to the new component and the API service, plus `dashboard-shell.component.spec.ts`.

**Interfaces:**
- Consumes:
  - Task 2's endpoint and JSON;
  - Task 3's `NO_EMPLOYEE_DETAIL` (not needed if the panel avoids those calls);
  - Task 4's wizard `selfPrefill` input and `saved` output;
  - `AuthStore.employeeId()`, `AuthStore.authenticated()` and `AuthStore.checkSession()`;
  - `DashboardStore.load(key)`.
- Produces:
  - `SelfSetupApiService.getStatus(): Observable<SelfSetupStatusDto>`, sent with `SILENT_HTTP_ERRORS` set to true. The DTO fields are the Task 2 JSON names.
  - `<app-no-employee-profile-panel (profileCreated)="..."/>`

- [ ] **Step 1: Write the failing specs:**
  - **Shell:** with `dashboardKey "my-work"`, `authenticated() === true` and `employeeId() === null`:
    - renders `[data-testid="no-employee-profile-panel"]`;
    - does **not** render `app-dashboard-grid`;
    - does not call `DashboardStore.load`.
  - **Shell:** for `"my-team"`, or when `employeeId()` is set, the grid renders as today.
  - **Panel:** status `can_add_self: true` shows the title, the ready text and the button. Clicking the button opens `app-add-employee-wizard` with `selfPrefill` set to `{ firstName, lastName, workEmail }` from the status.
  - **Panel:** status `can_add_self: false, has_employee_profile: false` shows the title and "Add a legal entity and a position first." with no button.
  - **Panel:** a status request error shows the title only, with no button and no thrown error.
  - **Panel:** the wizard emitting `saved` with `{ finalized: true, self: true }` calls `checkSession()` and then emits `profileCreated`. The shell then calls `DashboardStore.load("my-work")`.
- [ ] **Step 2: Run and see them fail.** Run: `npx ng test --watch=false --include "src/app/modules/dashboard/**/*.spec.ts"`.
- [ ] **Step 3: Implement** the model, API service, panel and shell branch. In the shell, the `@if` branch comes before `boot`, so the grid and its widget requests never start for a profile-less user. Use existing empty-state and button styles and the design tokens (no hard-coded colours; the colour guard is in lint).
- [ ] **Step 4: Run them and see them pass.** Then `npm run check:warnings` (expect "Build is clean") and `npx eslint` on the changed files (expect exit 0).
- [ ] **Step 5: Commit:** `feat(dashboard): calm My Work panel and "Add yourself as an employee" for accounts without a profile`

### Task 6: Ship and live-check

- [ ] **Step 1:** Push both branches and open the PRs:
  - backend `feat/owner-self-employee` → `development`;
  - web `feat/owner-self-employee` → `Development`.
  Each PR names its partner PR, and the web PR says it needs the backend endpoint.
- [ ] **Step 2: Live check after deploy to App Service**, signed in as the Unicom owner, in Chrome:
  - My Work shows the panel and no red toast;
  - with the org set up, **Add yourself as an employee** opens the prefilled wizard with the email locked;
  - finishing it shows the success copy, and My Work then loads the normal widgets without a sign-out;
  - no invitation email arrives.
